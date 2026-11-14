"""Measure complete installers on an inactive desktop; never start installation.

The timer covers process creation through a visible, responsive directory page.
It includes loader/security overhead, but not Explorer's pre-process checks.
Use identical payloads and compiler settings for a controlled before/after run.
"""
import argparse
import ctypes
from ctypes import wintypes as W
import hashlib
import json
from pathlib import Path
import statistics
import subprocess
import time
import uuid


class StartupInfo(ctypes.Structure):
    _fields_ = [('cb', W.DWORD), ('reserved', W.LPWSTR), ('desktop', W.LPWSTR),
                ('title', W.LPWSTR), ('x', W.DWORD), ('y', W.DWORD),
                ('xsize', W.DWORD), ('ysize', W.DWORD), ('xchars', W.DWORD),
                ('ychars', W.DWORD), ('fill', W.DWORD), ('flags', W.DWORD),
                ('show', W.WORD), ('reserved_size', W.WORD),
                ('reserved2', ctypes.c_void_p), ('stdin', W.HANDLE),
                ('stdout', W.HANDLE), ('stderr', W.HANDLE)]


class ProcessInfo(ctypes.Structure):
    _fields_ = [('process', W.HANDLE), ('thread', W.HANDLE),
                ('pid', W.DWORD), ('tid', W.DWORD)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, help='Optional baseline for a controlled comparison.')
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--before-rounds', type=int, help='Defaults to --rounds.')
    parser.add_argument('--max-ready-seconds', type=float, default=2)
    parser.add_argument('--ready-timeout-seconds', type=float, default=600)
    parser.add_argument('--capture-carousel', action='store_true')
    parser.add_argument('--reference-images', type=Path, help='Compare against previously captured before-role-N.png files.')
    parser.add_argument('--allow-disk-space-change', action='store_true', help='Exclude only the native disk-space value when payload contents change.')
    args = parser.parse_args()
    assert args.rounds >= 1
    before_rounds = (args.before_rounds if args.before_rounds is not None else args.rounds) if args.before else 0
    assert 0 <= before_rounds <= args.rounds
    args.output.mkdir(parents=True, exist_ok=True)
    user = ctypes.WinDLL('user32', use_last_error=True)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    user.SetProcessDPIAware()
    enum = ctypes.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
    user.CreateDesktopW.argtypes = [W.LPCWSTR, ctypes.c_void_p, ctypes.c_void_p,
                                   W.DWORD, W.DWORD, ctypes.c_void_p]
    user.CreateDesktopW.restype = W.HANDLE
    user.SetThreadDesktop.argtypes = [W.HANDLE]
    user.CloseDesktop.argtypes = [W.HANDLE]
    user.EnumDesktopWindows.argtypes = [W.HANDLE, enum, W.LPARAM]
    user.EnumChildWindows.argtypes = [W.HWND, enum, W.LPARAM]
    user.IsWindowVisible.argtypes = [W.HWND]
    user.GetClassNameW.argtypes = [W.HWND, W.LPWSTR, ctypes.c_int]
    user.GetWindowTextW.argtypes = [W.HWND, W.LPWSTR, ctypes.c_int]
    user.GetDlgCtrlID.argtypes = [W.HWND]
    user.GetWindowThreadProcessId.argtypes = [W.HWND, ctypes.POINTER(W.DWORD)]
    user.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM,
                                       W.UINT, W.UINT, ctypes.POINTER(ctypes.c_size_t)]
    user.SendMessageTimeoutW.restype = W.LPARAM
    user.PostMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]
    kernel.CreateProcessW.argtypes = [W.LPCWSTR, W.LPWSTR, ctypes.c_void_p,
                                     ctypes.c_void_p, W.BOOL, W.DWORD,
                                     ctypes.c_void_p, W.LPCWSTR,
                                     ctypes.POINTER(StartupInfo), ctypes.POINTER(ProcessInfo)]
    kernel.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
    kernel.CloseHandle.argtypes = [W.HANDLE]
    kernel.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]
    kernel.OpenProcess.restype = W.HANDLE
    kernel.TerminateProcess.argtypes = [W.HANDLE, W.UINT]
    desktop_name = 'DeskPet-Startup-' + uuid.uuid4().hex
    desktop = user.CreateDesktopW(desktop_name, None, None, 0, 0x10000000, None)
    assert desktop and user.SetThreadDesktop(desktop), ctypes.WinError(ctypes.get_last_error())

    def string(hwnd, class_name=False):
        buffer = ctypes.create_unicode_buffer(512)
        (user.GetClassNameW if class_name else user.GetWindowTextW)(hwnd, buffer, 512)
        return buffer.value

    def directory_page():
        found = []
        @enum
        def visit(hwnd, unused):
            if user.IsWindowVisible(hwnd) and string(hwnd, True) == 'TWizardForm':
                children = []
                @enum
                def child(control, unused):
                    if user.IsWindowVisible(control):
                        children.append((string(control, True), string(control)))
                    return True
                user.EnumChildWindows(hwnd, child, 0)
                captions = [text.replace('&', '') for _, text in children]
                if '开始安装' in captions and '恢复默认' in captions and any(
                        cls == 'TNewPathEdit' for cls, _ in children):
                    response = ctypes.c_size_t()
                    if user.SendMessageTimeoutW(hwnd, 0, 0, 0, 2, 100, ctypes.byref(response)):
                        found.append(hwnd)
            return True
        user.EnumDesktopWindows(desktop, visit, 0)
        return found[0] if found else None

    def snapshot(hwnd, path):
        from PIL import Image
        rect = W.RECT()
        user.GetWindowRect.argtypes = [W.HWND, ctypes.POINTER(W.RECT)]
        user.GetWindowRect(hwnd, ctypes.byref(rect))
        width, height = rect.right - rect.left, rect.bottom - rect.top
        gdi = ctypes.WinDLL('gdi32')
        user.GetWindowDC.argtypes = [W.HWND]; user.GetWindowDC.restype = W.HANDLE
        user.ReleaseDC.argtypes = [W.HWND, W.HANDLE]
        gdi.CreateCompatibleDC.argtypes = [W.HANDLE]; gdi.CreateCompatibleDC.restype = W.HANDLE
        gdi.CreateCompatibleBitmap.argtypes = [W.HANDLE, ctypes.c_int, ctypes.c_int]
        gdi.CreateCompatibleBitmap.restype = W.HANDLE
        gdi.SelectObject.argtypes = [W.HANDLE, W.HANDLE]; gdi.SelectObject.restype = W.HANDLE
        gdi.DeleteObject.argtypes = [W.HANDLE]; gdi.DeleteDC.argtypes = [W.HANDLE]
        user.PrintWindow.argtypes = [W.HWND, W.HANDLE, W.UINT]
        user.RedrawWindow.argtypes = [W.HWND, ctypes.c_void_p, W.HANDLE, W.UINT]
        gdi.GetDIBits.argtypes = [W.HANDLE, W.HANDLE, W.UINT, W.UINT,
                                 ctypes.c_void_p, ctypes.c_void_p, W.UINT]
        dc = user.GetWindowDC(hwnd); memory = gdi.CreateCompatibleDC(dc)
        bitmap = gdi.CreateCompatibleBitmap(dc, width, height)
        previous = gdi.SelectObject(memory, bitmap)
        try:
            user.RedrawWindow(hwnd, None, None, 0x185)
            assert user.PrintWindow(hwnd, memory, 2)
            header = (ctypes.c_int32 * 10)(40, width, -height, 1 | (32 << 16), 0, 0, 0, 0, 0, 0)
            data = ctypes.create_string_buffer(width * height * 4)
            assert gdi.GetDIBits(memory, bitmap, 0, height, data, header, 0) == height
            image = Image.frombuffer('RGB', (width, height), data, 'raw', 'BGRX', 0, 1)
            image.save(path)
            return image.copy()
        finally:
            gdi.SelectObject(memory, previous); gdi.DeleteObject(bitmap)
            gdi.DeleteDC(memory); user.ReleaseDC(hwnd, dc)

    def comparison_image(image):
        if not args.allow_disk_space_change:
            return image
        from PIL import ImageDraw
        result = image.copy()
        sx, sy = image.width / 1000, image.height / 636
        # Added/removed payload files legitimately change Inno's disk-space text.
        ImageDraw.Draw(result).rectangle((round(400*sx), round(578*sy), round(810*sx), round(598*sy)), fill='white')
        return result

    def capture_carousel(hwnd, name):
        from PIL import Image
        from PIL import ImageChops
        buttons = {}
        @enum
        def collect(control, unused):
            if string(control, True) == 'TNewButton':
                buttons[string(control)] = control
            return True
        user.EnumChildWindows(hwnd, collect, 0)
        images = []
        for index, caption in enumerate(('E包', '大肥鱼', '马尔芙', '小贝', '咕嘎')):
            assert caption in buttons, 'missing carousel control: ' + caption
            user.PostMessageW(buttons[caption], 0x201, 1, 5 | (5 << 16))
            user.PostMessageW(buttons[caption], 0x202, 0, 5 | (5 << 16))
            time.sleep(.25)
            path = args.output / f'{name}-role-{index}.png'
            captured = snapshot(hwnd, path)
            if name == 'after' and args.reference_images:
                expected = Image.open(args.reference_images / f'before-role-{index}.png').convert('RGB')
                header = round(expected.height * 54 / 636)
                region = (0, header, expected.width, expected.height)
                # A layered native button can be caught between paint messages.
                # Keep raw retries and require a settled matching capture.
                for attempt in range(10):
                    if ImageChops.difference(comparison_image(expected).crop(region), comparison_image(captured).crop(region)).getbbox() is None:
                        break
                    captured.save(args.output / f'{name}-role-{index}-unsettled-{attempt}.png')
                    time.sleep(.1)
                    captured = snapshot(hwnd, path)
            images.append(captured)
        width, height = images[0].size
        # Exclude navigation-dot focus rings when comparing automatic/manual states.
        hero = (0, int(height * .13), int(width * .36), int(height * .80))
        # Leave the last role selected, then verify an entire automatic cycle.
        for index in range(5):
            time.sleep(4.8)
            automatic = snapshot(hwnd, args.output / f'{name}-automatic-{index}.png')
            assert ImageChops.difference(automatic.crop(hero), images[index].crop(hero)).getbbox() is None, f'automatic carousel role {index} did not match manual selection'
        assert all(ImageChops.difference(images[0].crop(hero), item.crop(hero)).getbbox()
                   for item in images[1:]), 'carousel roles did not change'

    results = []
    report = {'measurement': 'CreateProcess to visible responsive directory page',
              'cache_policy': 'first observed launch then repeated launches; OS caches not flushed',
              'before_rounds': before_rounds, 'after_rounds': args.rounds,
              'disk_space_value_excluded': args.allow_disk_space_change,
              'installer_files': {}, 'samples': results, 'passed': False}
    for name in ('before', 'after'):
        if getattr(args, name) is None:
            continue
        path = getattr(args, name).resolve()
        with path.open('rb') as stream:
            hasher = hashlib.sha256()
            for block in iter(lambda: stream.read(1024 * 1024), b''):
                hasher.update(block)
            digest = hasher.hexdigest()
        report['installer_files'][name] = {'path': str(path), 'bytes': path.stat().st_size,
                                         'sha256': digest}
    try:
        for round_index in range(args.rounds):
            # Alternate the order to reduce ordering bias.
            for name in (('before', 'after') if round_index % 2 == 0 else ('after', 'before')):
                if name == 'before' and round_index >= before_rounds:
                    continue
                executable = getattr(args, name).resolve()
                si = StartupInfo(); si.cb = ctypes.sizeof(si); si.desktop = desktop_name
                info = ProcessInfo()
                command = ctypes.create_unicode_buffer(subprocess.list2cmdline([
                    str(executable), '/SP-', '/NORESTART',
                    '/DIR=' + str(((args.reference_images or args.output) / 'never-installed').resolve()),
                    '/LOG=' + str((args.output / f'{name}-{round_index}.log').resolve())]))
                started = time.perf_counter()
                assert kernel.CreateProcessW(None, command, None, None, False, 0, None,
                                             str(args.output.resolve()), ctypes.byref(si), ctypes.byref(info))
                kernel.CloseHandle(info.thread)
                hwnd = None
                try:
                    deadline = started + args.ready_timeout_seconds
                    while time.perf_counter() < deadline:
                        hwnd = directory_page()
                        if hwnd: break
                        time.sleep(.01)
                    assert hwnd, f'installer never displayed a ready directory page within {args.ready_timeout_seconds}s'
                    elapsed = time.perf_counter() - started
                    results.append({'variant': name, 'round': round_index + 1,
                                    'ready_seconds': elapsed})
                    print(json.dumps(results[-1]), flush=True)
                    if args.capture_carousel and round_index == 0:
                        capture_carousel(hwnd, name)
                    # Cancel, then answer any cancel confirmation; never click Install.
                    user.PostMessageW(hwnd, 0x10, 0, 0)
                    end = time.monotonic() + 15
                    while kernel.WaitForSingleObject(info.process, 50) != 0 and time.monotonic() < end:
                        @enum
                        def confirm(window, unused):
                            if user.IsWindowVisible(window) and string(window, True) in ('#32770', 'TNewMessageBox'):
                                @enum
                                def yes_button(control, unused):
                                    caption = string(control).replace('&', '')
                                    if user.IsWindowVisible(control) and user.GetDlgCtrlID(control) == 6 and caption in ('是', '是(Y)', 'Yes', 'Yes(Y)'):
                                        user.PostMessageW(control, 0xF5, 0, 0)  # BM_CLICK, IDYES
                                    return True
                                user.EnumChildWindows(window, yes_button, 0)
                            return True
                        user.EnumDesktopWindows(desktop, confirm, 0)
                    assert kernel.WaitForSingleObject(info.process, 1000) == 0, 'installer did not exit after cancel'
                finally:
                    if kernel.WaitForSingleObject(info.process, 0) != 0:
                        # All windows on this private desktop belong to this test.
                        @enum
                        def stop_owned(window, unused):
                            pid = W.DWORD()
                            user.GetWindowThreadProcessId(window, ctypes.byref(pid))
                            handle = kernel.OpenProcess(1, False, pid.value)
                            if handle:
                                kernel.TerminateProcess(handle, 1); kernel.CloseHandle(handle)
                            return True
                        user.EnumDesktopWindows(desktop, stop_owned, 0)
                        kernel.TerminateProcess(info.process, 1)
                        kernel.WaitForSingleObject(info.process, 3000)
                    kernel.CloseHandle(info.process)
        report['summary'] = {}
        for name in ('before', 'after'):
            values = [row['ready_seconds'] for row in results if row['variant'] == name]
            if not values:
                continue
            report['summary'][name] = {'median_seconds': statistics.median(values),
                                       'min_seconds': min(values), 'max_seconds': max(values)}
        report['startup_budget_seconds'] = args.max_ready_seconds
        report['startup_budget_met'] = report['summary']['after']['max_seconds'] <= args.max_ready_seconds
        if args.capture_carousel and (args.before or args.reference_images):
            from PIL import Image, ImageChops
            reference = args.reference_images or args.output
            for index in range(5):
                before = Image.open(reference / f'before-role-{index}.png').convert('RGB')
                after = Image.open(args.output / f'after-role-{index}.png').convert('RGB')
                assert before.size == after.size
                # Native layered close-button rendering can vary in PrintWindow.
                # Compare the complete body for every role, and the complete
                # title bar separately using role zero's settled capture.
                width, height = before.size
                header_height = round(height * 54 / 636)
                region = (0, header_height, width, height)
                assert ImageChops.difference(comparison_image(before).crop(region), comparison_image(after).crop(region)).getbbox() is None, f'role {index} UI changed'
                if index == 0:
                    region = (0, 0, width, header_height)
                    assert ImageChops.difference(before.crop(region), after.crop(region)).getbbox() is None, 'title bar changed'
            report['carousel'] = 'five page bodies and settled title bar match; manual selection and full automatic cycle passed'
        assert report['startup_budget_met'], 'startup exceeded ready-time budget'
        report['passed'] = True
    except BaseException as error:
        report['error'] = repr(error)
        raise
    finally:
        (args.output / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        user.CloseDesktop(desktop)


if __name__ == '__main__':
    main()
