{ Completion choices use the same Run command as src/StartupRegistration.cs.
  They deliberately never read or rewrite settings.json. }
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupValue = 'DafeiyuDeskPet';
var
  FinishDesktop, FinishStartup: TNewCheckBox;
  FinishLater: TNewButton;
  FinishNotice: TLabel;
  WasInstalled, OptionsLoaded, OptionsLoading, LaunchOnFinish, LaunchDone: Boolean;
  DesktopWasOwned, StartupWasOwned: Boolean;
  DesktopSnapshot: String;
  UninstallDesktopBackup: String;

function StartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\桌面伙伴.exe') + '" --startup';
end;

function DesktopLink: String;
begin
  Result := ExpandConstant('{autodesktop}\桌面伙伴.lnk');
end;

function OwnedDesktopLink: Boolean;
var Link: TUnicodeShortcut;
begin
  Result := ReadUnicodeShortcut(DesktopLink, Link);
  if not Result then Exit;
  Result := (CompareText(Link.Target, ExpandConstant('{app}\桌面伙伴.exe')) = 0)
    and (Link.Arguments = '') and (Link.Hotkey = 0)
    and (CompareText(Link.WorkingDirectory, ExpandConstant('{app}')) = 0)
    and ((Link.Description = '') or (Link.Description = '桌面伙伴'))
    and (Link.IconIndex = 0)
    and ((Link.Icon = '') or (CompareText(Link.Icon, ExpandConstant('{app}\桌面伙伴.exe')) = 0))
    and (Link.ShowCommand = 1);
end;

function HasExplicitDesktopTask: Boolean;
var I: Integer; Value: String;
begin
  Result := False;
  for I := 1 to ParamCount do begin
    Value := Uppercase(ParamStr(I));
    if (Pos('/TASKS=', Value) = 1) or (Pos('/MERGETASKS=', Value) = 1) then
      Result := True;
  end;
end;

function CreateSilentDesktopIcon: Boolean;
begin
  { An inherited task must not recreate an absent, opted-out desktop link.
    Explicit task flags may create it; existing custom links stay protected. }
  Result := WizardSilent and (OwnedDesktopLink or (HasExplicitDesktopTask and not FileExists(DesktopLink)));
end;

procedure LoadFinishOptions;
var Command: String; DesktopCanEdit, StartupCanEdit: Boolean;
begin
  if WizardSilent or OptionsLoaded or OptionsLoading then Exit;
  OptionsLoading := True;
  try
    DesktopWasOwned := OwnedDesktopLink;
    if DesktopWasOwned then
      try
        DesktopSnapshot := GetSHA256OfFile(DesktopLink);
      except
        DesktopWasOwned := False;
      end;
    DesktopCanEdit := not FileExists(DesktopLink) or DesktopWasOwned;
    FinishDesktop.Checked := DesktopWasOwned or ((not WasInstalled) and (not FileExists(DesktopLink)));
    if HasExplicitDesktopTask and DesktopCanEdit then
      FinishDesktop.Checked := WizardIsTaskSelected('desktopicon');
    StartupWasOwned := RegQueryStringValue(HKCU, StartupKey, StartupValue, Command)
      and (CompareText(Command, StartupCommand) = 0);
    FinishStartup.Checked := StartupWasOwned;
    StartupCanEdit := not RegValueExists(HKCU, StartupKey, StartupValue) or StartupWasOwned;
    if not DesktopCanEdit or not StartupCanEdit then
      FinishNotice.Caption := '已保留其他安装或自定义的快捷方式 / 启动项。'
    else
      FinishNotice.Caption := '点击下方完成按钮后应用这些设置。';
    OptionsLoaded := True;
    FinishDesktop.Enabled := DesktopCanEdit;
    FinishStartup.Enabled := StartupCanEdit;
  finally
    OptionsLoading := False;
  end;
end;

function ApplyFinishOptions: Boolean;
var Command, Link: String;
begin
  Result := False;
  try
    if FinishDesktop.Enabled then begin
      if FinishDesktop.Checked then begin
        if not FileExists(DesktopLink) then begin
          Link := CreateShellLink(DesktopLink, '桌面伙伴', ExpandConstant('{app}\桌面伙伴.exe'), '',
            ExpandConstant('{app}'), ExpandConstant('{app}\桌面伙伴.exe'), 0, SW_SHOWNORMAL);
          if (Link = '') or not FileExists(DesktopLink) then
            RaiseException('无法创建桌面快捷方式，请检查桌面文件夹权限后重试。');
          DesktopSnapshot := GetSHA256OfFile(DesktopLink);
          DesktopWasOwned := True;
        end else if not OwnedDesktopLink then
          RaiseException('桌面快捷方式已被其他安装或用户修改，请取消勾选后重试。');
      end else if DesktopWasOwned and OwnedDesktopLink then begin
        if (GetSHA256OfFile(DesktopLink) = DesktopSnapshot) and not DeleteFile(DesktopLink) then
          RaiseException('无法移除桌面快捷方式，请关闭占用它的程序后重试。');
      end;
    end;
    if FinishStartup.Enabled then begin
      if FinishStartup.Checked then begin
        { Recheck ownership at apply time in case another installation changed it. }
        if RegValueExists(HKCU, StartupKey, StartupValue) then begin
          if not RegQueryStringValue(HKCU, StartupKey, StartupValue, Command) then
            RaiseException('无法读取开机启动项，请检查权限后重试。');
          if CompareText(Command, StartupCommand) <> 0 then
            RaiseException('开机启动项已由其他安装修改，请取消勾选后重试。');
        end;
        if not RegWriteStringValue(HKCU, StartupKey, StartupValue, StartupCommand) then
          RaiseException('无法保存开机启动选项，请检查权限后重试。');
      end else if StartupWasOwned then begin
        if RegQueryStringValue(HKCU, StartupKey, StartupValue, Command) then
          if CompareText(Command, StartupCommand) = 0 then
            if not RegDeleteValue(HKCU, StartupKey, StartupValue) then
              RaiseException('无法关闭开机启动，请检查权限后重试。');
      end;
    end;
    Result := True;
  except
    MsgBox(GetExceptionMessage, mbError, MB_OK);
  end;
end;

procedure DeinitializeUninstall;
begin
  { Cancelling uninstall must not leave a redundant custom-link backup. }
  if (UninstallDesktopBackup = '') or not FileExists(UninstallDesktopBackup) then Exit;
  if FileExists(DesktopLink) then
    try
      if GetSHA256OfFile(DesktopLink) = GetSHA256OfFile(UninstallDesktopBackup) then
        DeleteFile(UninstallDesktopBackup);
    except
      Log('保留桌面快捷方式备份：' + UninstallDesktopBackup);
    end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var ErrorCode: Integer;
begin
  Result := True;
  if WizardSilent or (CurPageID <> wpFinished) then Exit;
  if not OptionsLoaded or OptionsLoading then begin Result := False; Exit; end;
  Result := ApplyFinishOptions;
  if not Result then Exit;
  if LaunchOnFinish and not LaunchDone then begin
    Result := ShellExec('open', ExpandConstant('{app}\桌面伙伴.exe'), '', ExpandConstant('{app}'),
      SW_SHOWNORMAL, ewNoWait, ErrorCode);
    if Result then LaunchDone := True
    else MsgBox('无法启动桌面伙伴，请重试或选择稍后启动。' + #13#10 + SysErrorMessage(ErrorCode), mbError, MB_OK);
  end;
end;

procedure FinishLaterClick(Sender: TObject);
begin
  LaunchOnFinish := False;
  WizardForm.NextButton.OnClick(WizardForm.NextButton);
  LaunchOnFinish := True;
end;

function InitializeUninstall: Boolean;
var I: Integer; Candidate: String;
begin
  Result := True;
  if FileExists(DesktopLink) and not OwnedDesktopLink then begin
    { Older installers logged this icon for unconditional removal. Preserve its
      exact bytes before their log executes, without editing the uninstall log.
      A same-directory backup also survives an unexpected uninstall failure. }
    try
      Result := False;
      for I := 0 to 99 do begin
        Candidate := DesktopLink + '.deskpet-preserved-' + IntToStr(I);
        if not FileExists(Candidate) and not DirExists(Candidate) then begin
          Result := FileCopy(DesktopLink, Candidate, True);
          if Result then UninstallDesktopBackup := Candidate;
          Break;
        end;
      end;
    except
      Result := False;
    end;
    if not Result then SuppressibleMsgBox('无法保护你自定义的桌面快捷方式，已停止卸载。请检查桌面文件夹权限后重试。', mbError, MB_OK, IDOK);
  end;
end;

procedure RestoreCustomDesktopLink;
begin
  if UninstallDesktopBackup = '' then Exit;
  try
    if not FileExists(DesktopLink) then
      if not FileCopy(UninstallDesktopBackup, DesktopLink, True) then
        RaiseException('无法恢复自定义桌面快捷方式。');
    if GetSHA256OfFile(DesktopLink) = GetSHA256OfFile(UninstallDesktopBackup) then
      DeleteFile(UninstallDesktopBackup)
    else Log('桌面快捷方式在卸载期间发生变化，原文件备份保留在：' + UninstallDesktopBackup);
  except
    Log('无法恢复自定义桌面快捷方式，备份保留在：' + UninstallDesktopBackup);
    SuppressibleMsgBox('无法恢复自定义桌面快捷方式。原文件备份保留在：' + #13#10 + UninstallDesktopBackup, mbError, MB_OK, IDOK);
  end;
end;
