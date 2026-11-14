{ Explicit Unicode COM avoids WSH's ACP-dependent filename and field conversion.
  Interface order follows Inno Setup's official CodeAutomation2 example. }
const
  CLSID_UnicodeShellLink = '{00021401-0000-0000-C000-000000000046}';
type
  IUnicodeShellLink = interface(IUnknown)
    '{000214F9-0000-0000-C000-000000000046}'
    function GetPath(Value: String; Count: Integer; FindData: Longint; Flags: DWORD): HResult;
    procedure GetIDListUnused;
    procedure SetIDListUnused;
    function GetDescription(Value: String; Count: Integer): HResult;
    procedure SetDescriptionUnused;
    function GetWorkingDirectory(Value: String; Count: Integer): HResult;
    procedure SetWorkingDirectoryUnused;
    function GetArguments(Value: String; Count: Integer): HResult;
    procedure SetArgumentsUnused;
    function GetHotkey(out Value: Word): HResult;
    procedure SetHotkeyUnused;
    function GetShowCmd(out Value: Integer): HResult;
    procedure SetShowCmdUnused;
    function GetIconLocation(Value: String; Count: Integer; out Index: Integer): HResult;
  end;
  IShortcutPersist = interface(IUnknown)
    '{0000010C-0000-0000-C000-000000000046}'
    procedure GetClassIDUnused;
  end;
  IShortcutPersistFile = interface(IShortcutPersist)
    '{0000010B-0000-0000-C000-000000000046}'
    procedure IsDirtyUnused;
    function Load(FileName: String; Mode: Longint): HResult;
  end;
  TUnicodeShortcut = record
    Target, WorkingDirectory, Arguments, Description, Icon: String;
    Hotkey: Word;
    ShowCommand, IconIndex: Integer;
  end;

function ShortcutText(Value: String): String;
var Terminator: Integer;
begin
  for Terminator := 1 to Length(Value) do
    if Value[Terminator] = #0 then begin
      SetLength(Value, Terminator - 1); Result := Value; Exit;
    end;
  RaiseException('Unterminated shortcut metadata.');
end;

function ReadUnicodeShortcut(Path: String; var Info: TUnicodeShortcut): Boolean;
var Obj: IUnknown; Link: IUnicodeShellLink; FileReader: IShortcutPersistFile;
begin
  Result := False;
  if not FileExists(Path) then Exit;
  try
    Obj := CreateComObject(StringToGuid(CLSID_UnicodeShellLink));
    FileReader := IShortcutPersistFile(Obj); OleCheck(FileReader.Load(Path, 0));
    Link := IUnicodeShellLink(Obj);
    Info.Target := StringOfChar(#0, 32768);
    { GetPath explicitly permits a NULL WIN32_FIND_DATAW pointer. RAWPATH does
      not resolve or adopt a different target when an old executable is gone. }
    OleCheck(Link.GetPath(Info.Target, Length(Info.Target), 0, 4));
    Info.Target := ShortcutText(Info.Target);
    if Info.Target = '' then Exit;
    Info.WorkingDirectory := StringOfChar(#0, 32768);
    OleCheck(Link.GetWorkingDirectory(Info.WorkingDirectory, Length(Info.WorkingDirectory)));
    Info.WorkingDirectory := ShortcutText(Info.WorkingDirectory);
    Info.Arguments := StringOfChar(#0, 32768);
    OleCheck(Link.GetArguments(Info.Arguments, Length(Info.Arguments)));
    Info.Arguments := ShortcutText(Info.Arguments);
    Info.Description := StringOfChar(#0, 32768);
    OleCheck(Link.GetDescription(Info.Description, Length(Info.Description)));
    Info.Description := ShortcutText(Info.Description);
    Info.Icon := StringOfChar(#0, 32768);
    OleCheck(Link.GetIconLocation(Info.Icon, Length(Info.Icon), Info.IconIndex));
    Info.Icon := ShortcutText(Info.Icon);
    OleCheck(Link.GetHotkey(Info.Hotkey)); OleCheck(Link.GetShowCmd(Info.ShowCommand));
    Result := True;
  except
    { Unknown, corrupt or unreadable links remain protected. }
  end;
end;
