using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Dafeiyu
{
    // Native Explorer dialog: normal multi-file selection plus an explicit current-folder action.
#if PORTABLE
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public static class AttachmentPicker
    {
        private const uint AddFolderButton = 1001;
        public static string[] Show(IntPtr owner)
        {
            return Show(owner, null);
        }
        internal static string[] Show(IntPtr owner, string initialDirectory)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException(TextResources.Get("attachmentPicker.show.text"));
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException(TextResources.Get("attachmentPicker.show.text.alternativeA"));
            IFileOpenDialog dialog = null; DialogEvents events = null; uint cookie = 0; bool advised = false;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                dialog.SetOptions(0x40 | 0x200 | 0x800 | 0x1000 | 0x8 | 0x02000000);
                dialog.SetTitle(TextResources.Get("attachmentPicker.show.dialogSetTitle"));
                dialog.SetOkButtonLabel(TextResources.Get("attachmentPicker.show.dialogSetOkButtonLabel"));
                if (!String.IsNullOrEmpty(initialDirectory))
                {
                    IShellItem initial = null;
                    try { Guid itemId = typeof(IShellItem).GUID; SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref itemId, out initial); dialog.SetFolder(initial); }
                    finally { Release(initial); }
                }
                var customize = (IFileDialogCustomize)dialog;
                customize.AddPushButton(AddFolderButton, TextResources.Get("attachmentPicker.show.customizeAddPushButton"));
                events = new DialogEvents(dialog);
                dialog.Advise(events, out cookie); advised = true;
                int status = dialog.Show(owner);
                if (status == unchecked((int)0x800704C7)) return new string[0];
                if (events.Error != null) throw new InvalidOperationException(events.Error);
                if (status < 0) throw new InvalidOperationException(TextResources.Get("attachmentPicker.show.text.alternativeB"));
                if (events.SelectedFolder != null) return new[] { events.SelectedFolder };
                IShellItemArray items = null;
                try
                {
                    dialog.GetResults(out items); uint count; items.GetCount(out count);
                    if (count > 1000) throw new InvalidOperationException(TextResources.Get("attachmentPicker.show.text.alternativeC"));
                    var result = new List<string>();
                    for (uint i = 0; i < count; i++)
                    {
                        IShellItem item = null;
                        try { items.GetItemAt(i, out item); result.Add(FileSystemPath(item)); }
                        finally { Release(item); }
                    }
                    return result.ToArray();
                }
                finally { Release(items); }
            }
            catch (COMException) { throw new InvalidOperationException(TextResources.Get("attachmentPicker.show.text.alternativeD")); }
            finally
            {
                if (advised) { try { dialog.Unadvise(cookie); } catch (COMException) { } }
                Release(dialog);
                GC.KeepAlive(events);
            }
        }

        private static string FileSystemPath(IShellItem item)
        {
            IntPtr text = IntPtr.Zero;
            try
            {
                item.GetDisplayName(0x80058000, out text);
                string result = Marshal.PtrToStringUni(text);
                if (String.IsNullOrEmpty(result)) throw new InvalidOperationException(TextResources.Get("attachmentPicker.fileSystemPath.text"));
                return result;
            }
            finally { if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text); }
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        private sealed class DialogEvents : IFileDialogEvents, IFileDialogControlEvents
        {
            private readonly IFileOpenDialog dialog;
            internal string SelectedFolder, Error;
            internal DialogEvents(IFileOpenDialog dialog) { this.dialog = dialog; }
            public void OnButtonClicked(IntPtr customize, uint id)
            {
                if (id != AddFolderButton) return;
                IShellItem folder = null;
                try { dialog.GetFolder(out folder); SelectedFolder = FileSystemPath(folder); dialog.Close(0); }
                catch (Exception) { Error = TextResources.Get("attachmentPicker.onButtonClicked.error"); dialog.Close(unchecked((int)0x80004005)); }
                finally { Release(folder); }
            }
            public int OnFileOk(IntPtr dialog) { return 0; }
            public int OnFolderChanging(IntPtr dialog, IntPtr folder) { return 0; }
            public void OnFolderChange(IntPtr dialog) { }
            public void OnSelectionChange(IntPtr dialog) { }
            public void OnShareViolation(IntPtr dialog, IntPtr item, out uint response) { response = 0; }
            public void OnTypeChange(IntPtr dialog) { }
            public void OnOverwrite(IntPtr dialog, IntPtr item, out uint response) { response = 0; }
            public void OnItemSelected(IntPtr customize, uint control, uint item) { }
            public void OnCheckButtonToggled(IntPtr customize, uint control, bool check) { }
            public void OnControlActivating(IntPtr customize, uint control) { }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")] private class FileOpenDialog { }
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IFileDialogEvents events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }
        [ComImport, Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialogCustomize
        {
            void EnableOpenDropDown(uint id);
            void AddMenu(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
            void AddPushButton(uint id, [MarshalAs(UnmanagedType.LPWStr)] string text);
        }
        [ComImport, Guid("973510DB-7D7F-452B-8975-74A85828D354"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialogEvents
        {
            [PreserveSig] int OnFileOk(IntPtr dialog);
            [PreserveSig] int OnFolderChanging(IntPtr dialog, IntPtr folder);
            void OnFolderChange(IntPtr dialog);
            void OnSelectionChange(IntPtr dialog);
            void OnShareViolation(IntPtr dialog, IntPtr item, out uint response);
            void OnTypeChange(IntPtr dialog);
            void OnOverwrite(IntPtr dialog, IntPtr item, out uint response);
        }
        [ComImport, Guid("36116642-D713-4B97-9B83-7484A9D00433"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialogControlEvents
        {
            void OnItemSelected(IntPtr customize, uint control, uint item);
            void OnButtonClicked(IntPtr customize, uint control);
            void OnCheckButtonToggled(IntPtr customize, uint control, [MarshalAs(UnmanagedType.Bool)] bool check);
            void OnControlActivating(IntPtr customize, uint control);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetPropertyStore(int flags, ref Guid iid, out IntPtr store);
            void GetPropertyDescriptionList(ref IntPtr key, ref Guid iid, out IntPtr list);
            void GetAttributes(uint flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr enumerator);
        }
    }
}
