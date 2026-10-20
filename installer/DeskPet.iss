#ifndef RuntimeDir
  #error Build with BuildInstaller.ps1
#endif
#ifndef ProductVersion
  #error Missing ProductVersion
#endif
#ifndef RepositoryUrl
  #error Missing RepositoryUrl
#endif
#ifndef InstallerBaseName
  #error Missing InstallerBaseName
#endif

[Setup]
AppId={{3B63C162-076E-4B2F-BDCC-2572664875A9}
AppName=桌面伙伴
AppVersion={#ProductVersion}
AppPublisher=彧晟Eason
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases/latest
DefaultDirName={localappdata}\Programs\EbaoDeskPet
UsePreviousAppDir=yes
DefaultGroupName=桌面伙伴
UsePreviousGroup=no
DisableProgramGroupPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Local\Dafeiyu.DeskPet.Installation
CloseApplications=no
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename={#InstallerBaseName}
SetupIconFile=..\assets\pet.ico
UninstallDisplayIcon={app}\桌面伙伴.exe
UninstallDisplayName=桌面伙伴
VersionInfoVersion={#AssemblyVersion}
VersionInfoProductName=桌面伙伴
VersionInfoDescription=桌面伙伴安装程序
VersionInfoCompany=彧晟Eason
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
DisableWelcomePage=no
Uninstallable=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#RuntimeDir}\桌面伙伴.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\桌面伙伴.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\DeskPet.Update.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\DeskPet.Update.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\configuration-guide.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\agent-workflows.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\third-party-notices.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\native-agent-design.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\conversation-agent.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\skin-format.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\character-packs.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\dialogue-setup.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\installation.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\daily-use.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\voice-presets.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\sources.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\roadmap.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\release-naming.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\docs\interaction-extension.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RuntimeDir}\templates\*.png"; DestDir: "{app}\templates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\templates\*.json"; DestDir: "{app}\templates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\templates\*.md"; DestDir: "{app}\templates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\templates\*.svg"; DestDir: "{app}\templates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\previews\*.png"; DestDir: "{app}\previews"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\previews\*.jpg"; DestDir: "{app}\previews"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\previews\*.webp"; DestDir: "{app}\previews"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RuntimeDir}\previews\*.svg"; DestDir: "{app}\previews"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[InstallDelete]
Type: files; Name: "{app}\大肥鱼桌宠.exe"
Type: files; Name: "{app}\大肥鱼桌宠.exe.config"

[Icons]
Name: "{group}\桌面伙伴"; Filename: "{app}\桌面伙伴.exe"; WorkingDir: "{app}"
Name: "{group}\卸载桌面伙伴"; Filename: "{uninstallexe}"
Name: "{autodesktop}\桌面伙伴"; Filename: "{app}\桌面伙伴.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\桌面伙伴.exe"; Description: "启动桌面伙伴"; Flags: nowait postinstall skipifsilent

[Code]
var
  PreviousIconGroup: String;

procedure RemoveLegacyShortcut(Path, Target: String);
var
  Shell, Shortcut: Variant;
begin
  if not FileExists(Path) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(Path);
    if CompareText(Shortcut.TargetPath, Target) = 0 then DeleteFile(Path);
  except
    { A user-modified or unreadable shortcut is left untouched. }
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  RegisteredCommand, LegacyCommand, NewCommand: String;
begin
  if CurStep = ssInstall then
    RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B63C162-076E-4B2F-BDCC-2572664875A9}_is1', 'Inno Setup: Icon Group', PreviousIconGroup);
  if CurStep = ssPostInstall then
  begin
    LegacyCommand := '"' + ExpandConstant('{app}\大肥鱼桌宠.exe') + '" --startup';
    NewCommand := '"' + ExpandConstant('{app}\桌面伙伴.exe') + '" --startup';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DafeiyuDeskPet', RegisteredCommand) then
      if CompareText(RegisteredCommand, LegacyCommand) = 0 then
        RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DafeiyuDeskPet', NewCommand);
    RemoveLegacyShortcut(ExpandConstant('{userprograms}\大肥鱼桌面伙伴\大肥鱼桌面伙伴.lnk'), ExpandConstant('{app}\大肥鱼桌宠.exe'));
    RemoveLegacyShortcut(ExpandConstant('{userprograms}\大肥鱼桌面伙伴\卸载大肥鱼桌面伙伴.lnk'), ExpandConstant('{app}\unins000.exe'));
    RemoveLegacyShortcut(ExpandConstant('{autodesktop}\大肥鱼桌面伙伴.lnk'), ExpandConstant('{app}\大肥鱼桌宠.exe'));
    if PreviousIconGroup <> '' then
    begin
      RemoveLegacyShortcut(AddBackslash(ExpandConstant('{userprograms}')) + AddBackslash(PreviousIconGroup) + '大肥鱼桌面伙伴.lnk', ExpandConstant('{app}\大肥鱼桌宠.exe'));
      RemoveLegacyShortcut(AddBackslash(ExpandConstant('{userprograms}')) + AddBackslash(PreviousIconGroup) + '卸载大肥鱼桌面伙伴.lnk', ExpandConstant('{app}\unins000.exe'));
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('请先安装 Microsoft .NET Framework 4.8 或更新版本，再运行此安装包。', mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RegisteredCommand, ExpectedCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    ExpectedCommand := '"' + ExpandConstant('{app}\桌面伙伴.exe') + '" --startup';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DafeiyuDeskPet', RegisteredCommand) then
      if CompareText(RegisteredCommand, ExpectedCommand) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DafeiyuDeskPet');
  end;
end;
