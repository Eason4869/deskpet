const
  UninstallWizardWidth = 820;
  UninstallWizardHeight = 488;
  UninstallRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B63C162-076E-4B2F-BDCC-2572664875A9}_is1';
  UninstallCardColor = $00FFF4ED;
  UninstallAccent = $00ED6845;
  UninstallMuted = $0090786C;

var
  ShowUninstallWizard: Boolean;
  UninstallWorker: THandle;
  UninstallArt, UninstallLogo: TPngImage;
  UninstallCompletion: TSetupForm;
  UninstallRemoveData: TNewCheckBox;
  UninstallCleanupStatus: TNewStaticText;
  UninstallProgressFill, UninstallProgressTrack: TPanel;
  UninstallPercent: TNewStaticText;
  UninstallProgressTimer: LongWord;

function OpenUninstallWorker(Access: LongWord; Inherit: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitUninstallWorker(Worker: THandle; Timeout: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseUninstallWorker(Worker: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
procedure RegisterUninstallWizard;
var Exe, QuotedExe, Command: String;
begin
  Exe := ExpandConstant('{uninstallexe}');
  QuotedExe := '"' + Exe + '"';
  if RegQueryStringValue(HKCU, UninstallRegistryKey, 'UninstallString', Command) then
    if (Command = Exe) or (Command = QuotedExe) then
      if not RegWriteStringValue(HKCU, UninstallRegistryKey, 'UninstallString', QuotedExe + ' {#UninstallWizardFlags}') then
        Log('Could not register the DeskPet uninstall wizard.');
end;

function HasUninstallArgument(Argument: String): Boolean;
var I: Integer;
begin
  Result := True;
  for I := 1 to ParamCount do
    if SameText(ParamStr(I), Argument) then Exit;
  Result := False;
end;

function InitializeUninstallWizard: Boolean;
var WorkerId: Integer;
begin
  { Registered interactive entries pass /SILENT to suppress Inno's own
    completion dialog. Keep the built-in flow for bare and unattended runs. }
  ShowUninstallWizard := UninstallSilent and HasUninstallArgument('/DESKPETUNINSTALLUI') and
    not HasUninstallArgument('/VERYSILENT') and not HasUninstallArgument('/SUPPRESSMSGBOXES');
  Result := True;
  if not ShowUninstallWizard then Exit;
  if HasUninstallArgument('/DESKPETUNINSTALLCONFIRM') then
    Result := MsgBox(FmtMessage(SetupMessage(msgConfirmUninstall), ['桌面伙伴']),
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if not Result then Exit;
  WorkerId := StrToIntDef(ExpandConstant('{param:DESKPETUNINSTALLWORKER|0}'), 0);
  if WorkerId > 0 then
    UninstallWorker := OpenUninstallWorker($00100000, False, WorkerId);
end;

function LoadUninstallArtwork(Name: String): TPngImage;
var Path: String;
begin
  Result := nil;
  Path := ExpandConstant('{app}\uninstall-ui\') + Name;
  if not FileExists(Path) or not OrdinaryPath(Path) then Exit;
  Result := TPngImage.Create;
  try
    Result.LoadFromFile(Path);
  except
    Result.Free;
    Result := nil;
    Log('Could not load uninstall artwork: ' + Name);
  end;
end;

function UninstallPanel(Parent: TWinControl; Color: TColor; X, Y, Width, Height: Integer): TPanel;
begin
  Result := TPanel.Create(Parent);
  Result.Parent := Parent;
  Result.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(Width), ScaleY(Height));
  Result.BevelOuter := bvNone;
  Result.ParentBackground := False;
  Result.Color := Color;
end;

function UninstallLabel(Parent: TWinControl; Caption: String; X, Y, Width, Height, Size: Integer): TLabel;
begin
  Result := UiLabel(Parent, Caption, X, Y, Width, Height, Size);
  if HighContrastActive then Result.Font.Color := clWindowText;
end;

procedure AddUninstallArtwork(Parent: TWinControl; Artwork: TPngImage; X, Y, Width, Height: Integer);
var Image: TBitmapImage;
begin
  if Artwork = nil then Exit;
  Image := TBitmapImage.Create(Parent);
  Image.Parent := Parent;
  Image.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(Width), ScaleY(Height));
  Image.Stretch := True;
  Image.PngImage.Assign(Artwork);
end;

procedure CloseUninstallCompletion(Sender: TObject);
begin
  UninstallCompletion.Close;
end;

procedure UninstallCompletionKeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  if Key = 27 then begin
    Key := 0;
    CloseUninstallCompletion(Sender);
  end;
end;

procedure KeepUninstallProgressOpen(Sender: TObject; var CanClose: Boolean);
begin
  CanClose := False;
end;

procedure AddUninstallCloseButton(Header: TPanel; Enabled: Boolean; Background: TColor);
var Button: TBitmapButton; Size, Inset: Integer;
begin
  Button := TBitmapButton.Create(Header);
  Button.Parent := Header;
  Button.SetBounds(ScaleX(768), ScaleY(7), ScaleX(42), ScaleY(40));
  Button.Caption := '关闭';
  Button.BackColor := Background;
  Button.Enabled := Enabled;
  Button.TabStop := Enabled;
  if Enabled then Button.OnClick := @CloseUninstallCompletion;
  Size := ScaleX(15);
  Inset := ScaleX(3);
  Button.Bitmap.Width := Size;
  Button.Bitmap.Height := Size;
  Button.Bitmap.Canvas.Brush.Color := Background;
  Button.Bitmap.Canvas.Pen.Color := Background;
  Button.Bitmap.Canvas.Rectangle(0, 0, Size, Size);
  if Enabled then Button.Bitmap.Canvas.Pen.Color := $00774D36
  else Button.Bitmap.Canvas.Pen.Color := $00D5CAC3;
  if HighContrastActive then begin
    if Enabled then Button.Bitmap.Canvas.Pen.Color := clWindowText
    else Button.Bitmap.Canvas.Pen.Color := clGrayText;
  end;
  Button.Bitmap.Canvas.MoveTo(Inset, Inset);
  Button.Bitmap.Canvas.LineTo(Size-Inset, Size-Inset);
  Button.Bitmap.Canvas.MoveTo(Inset, Size-Inset);
  Button.Bitmap.Canvas.LineTo(Size-Inset, Inset);
end;

procedure BuildUninstallShell(Form: TSetupForm; CanClose: Boolean);
var Header, Hero: TPanel; Brand, Version: TLabel; Background: TColor;
begin
  Form.Font.Name := 'Microsoft YaHei UI';
  if HighContrastActive then Background := clWindow else Background := clWhite;
  Form.Color := Background;
  Form.BorderStyle := bsNone;
  Form.ClientWidth := ScaleX(UninstallWizardWidth);
  Form.ClientHeight := ScaleY(UninstallWizardHeight);
  RoundWizardFrame(Form);
  Header := UninstallPanel(Form, Background, 0, 0, UninstallWizardWidth, 54);
  InitializeHeaderDrag(Header, Form);
  AddUninstallCloseButton(Header, CanClose, Background);
  AddUninstallArtwork(Header, UninstallLogo, 22, 12, 30, 30);
  Brand := UninstallLabel(Header, '桌面伙伴 · 卸载向导', 64, 17, 185, 26, 11);
  Brand.Font.Style := [fsBold];
  Version := UninstallLabel(Header, '{#ProductVersion}', 244, 19, 170, 24, 9);
  if HighContrastActive then Exit;
  Version.Font.Color := UninstallMuted;
  UninstallPanel(Form, $00EFE3DC, 0, 54, UninstallWizardWidth, 1);
  Hero := UninstallPanel(Form, UninstallCardColor, 0, 55, 267, UninstallWizardHeight-55);
  AddUninstallArtwork(Hero, UninstallArt, 0, 0, 267, UninstallWizardHeight-55);
end;

procedure RefreshUninstallProgress(Window: HWND; Msg: Cardinal; Id: LongWord; Time: Cardinal);
var Bar: TNewProgressBar; Percent: Integer;
begin
  if (UninstallProgressTimer = 0) or (Id <> UninstallProgressTimer) then Exit;
  Bar := UninstallProgressForm.ProgressBar;
  Percent := 0;
  if Bar.Max > Bar.Min then Percent := MulDiv(Bar.Position-Bar.Min, 100, Bar.Max-Bar.Min);
  UninstallPercent.Caption := IntToStr(Percent) + '%';
  UninstallProgressFill.Width := MulDiv(UninstallProgressTrack.Width, Percent, 100);
  UninstallProgressFill.Visible := Percent > 0;
end;

procedure StopUninstallProgress(Sender: TObject);
begin
  if UninstallProgressTimer <> 0 then begin
    KillCarouselTimer(0, UninstallProgressTimer);
    UninstallProgressTimer := 0;
  end;
  StopHeaderDrag;
end;

procedure InitializeUninstallProgressForm;
var Form: TUninstallProgressForm; Heading: TLabel;
begin
  if not ShowUninstallWizard then Exit;
  if not HighContrastActive then begin
    UninstallLogo := LoadUninstallArtwork('deskpet-logo.png');
    UninstallArt := LoadUninstallArtwork('e-bao-panel.png');
  end;
  Form := UninstallProgressForm;
  Form.Caption := '桌面伙伴 · 正在卸载';
  Form.ClientWidth := ScaleX(UninstallWizardWidth);
  Form.ClientHeight := ScaleY(UninstallWizardHeight);
  Form.BorderIcons := [biSystemMenu, biMinimize];
  Form.MainPanel.Hide;
  Form.OuterNotebook.Hide;
  Form.Bevel.Hide;
  Form.Bevel1.Hide;
  Form.BeveledLabel.Hide;
  Form.CancelButton.Hide;
  BuildUninstallShell(Form, False);
  Form.ProgressBar.Parent := Form;
  Form.ProgressBar.SetBounds(ScaleX(307), ScaleY(234), ScaleX(468), ScaleY(10));
  Form.OnCloseQuery := @KeepUninstallProgressOpen;
  Form.OnDestroy := @StopUninstallProgress;
  Heading := UninstallLabel(Form, '正在卸载桌面伙伴', 307, 123, 468, 60, 24);
  Heading.Font.Style := [fsBold];
  UninstallLabel(Form, '正在移除程序文件和快捷方式，请稍候。', 307, 189, 468, 35, 11);
  UninstallLabel(Form, '卸载完成后，你可以选择是否删除应用数据。', 307, 263, 468, 35, 10);
  if not HighContrastActive then begin
    UninstallProgressTrack := UninstallPanel(Form, UninstallCardColor, 307, 234, 468, 9);
    UninstallProgressFill := UninstallPanel(UninstallProgressTrack, UninstallAccent, 0, 0, 0, 9);
    UninstallPercent := TNewStaticText.Create(Form);
    UninstallPercent.Parent := Form;
    UninstallPercent.SetBounds(ScaleX(715), ScaleY(261), ScaleX(60), ScaleY(28));
    UninstallPercent.AutoSize := False;
    UninstallPercent.Alignment := taRightJustify;
    UninstallPercent.Font.Color := UninstallAccent;
    UninstallPercent.Caption := '0%';
    UninstallProgressTimer := SetCarouselTimer(0, 0, 60, CreateCallback(@RefreshUninstallProgress));
    if UninstallProgressTimer <> 0 then Form.ProgressBar.Hide
    else begin
      UninstallProgressTrack.Hide;
      UninstallPercent.Hide;
    end;
  end;
end;

function SafeUninstallDataPath(Path: String): Boolean;
var Parent, Current: String; Attributes: LongWord;
begin
  Result := False;
  Attributes := FileAttributes(Path);
  if (Attributes = $FFFFFFFF) or ((Attributes and $10) = 0) then Exit;
  Current := ExtractFileDir(Path);
  if SameText(AddBackslash(ExtractFileDrive(Current)), AddBackslash(Current)) then Exit;
  while Current <> '' do begin
    Attributes := FileAttributes(Current);
    if (Attributes = $FFFFFFFF) or ((Attributes and $400) <> 0) then Exit;
    Parent := ExtractFileDir(Current);
    if SameText(Parent, Current) then Break;
    Current := Parent;
  end;
  Result := (FileAttributes(Path) and $400) = 0;
end;

function UninstallProgramRemoved: Boolean;
begin
  Result := not FileExists(ExpandConstant('{app}\桌面伙伴.exe')) and
    not FileExists(ExpandConstant('{app}\大肥鱼桌宠.exe')) and
    not FileExists(ExpandConstant('{app}\DeskPet.Update.exe')) and
    not CheckForMutexes('Local\Dafeiyu.DeskPet.Installation');
end;

procedure FinishUninstallClick(Sender: TObject);
var Path: String;
begin
  if not UninstallRemoveData.Checked then begin
    UninstallCompletion.Close;
    Exit;
  end;
  { The original Inno executable exits before this page. Wait for the
    application handoff worker to finish writing its receipt and release data. }
  if (UninstallWorker <> 0) and (WaitUninstallWorker(UninstallWorker, 4000) <> 0) then begin
    UninstallCleanupStatus.Caption := '卸载助手仍在退出。请稍后重试，或取消勾选以关闭。';
    Exit;
  end;
  Path := ExpandFileName(ExpandConstant('{app}\data'));
  if UninstallProgramRemoved and SafeUninstallDataPath(Path) then begin
    UninstallCleanupStatus.Caption := '正在删除应用数据文件…';
    UninstallCompletion.Update;
    if DelTree(Path, True, True, True) then begin
      RemoveDir(ExtractFileDir(Path));
      UninstallCompletion.Close;
      Exit;
    end;
    UninstallCleanupStatus.Caption := '部分数据未能删除。请关闭占用文件的程序后重试。';
  end else
    UninstallCleanupStatus.Caption := '无法安全清理该数据目录。请取消勾选以关闭。';
end;

procedure ShowUninstallCompletion;
var Heading, Detail: TLabel; Options: TWinControl; Finish: TNewButton; Path: String; Removed: Boolean;
begin
  if not ShowUninstallWizard then Exit;
  Path := ExpandFileName(ExpandConstant('{app}\data'));
  Removed := UninstallProgramRemoved;
  UninstallCompletion := CreateCustomForm(UninstallWizardWidth, UninstallWizardHeight, True, True);
  try
    UninstallCompletion.Caption := '桌面伙伴 · 卸载完成';
    UninstallCompletion.BorderStyle := bsSingle;
    UninstallCompletion.BorderIcons := [biSystemMenu];
    UninstallCompletion.Position := poScreenCenter;
    BuildUninstallShell(UninstallCompletion, True);
    UninstallCompletion.KeyPreview := True;
    UninstallCompletion.OnKeyDown := @UninstallCompletionKeyDown;
    Heading := UninstallLabel(UninstallCompletion, '卸载完成', 307, 100, 468, 59, 24);
    Heading.Font.Style := [fsBold];
    Detail := UninstallLabel(UninstallCompletion, '桌面伙伴主程序已移除。', 307, 165, 468, 32, 11);
    if not Removed then Detail.Caption := '部分程序文件未能移除，请检查安装目录。';

    if HighContrastActive then Options := UninstallPanel(UninstallCompletion, clWindow, 307, 219, 468, 117)
    else Options := UninstallPanel(UninstallCompletion, UninstallCardColor, 307, 219, 468, 117);
    UninstallRemoveData := TNewCheckBox.Create(Options);
    UninstallRemoveData.Parent := Options;
    UninstallRemoveData.SetBounds(ScaleX(18), ScaleY(15), ScaleX(429), ScaleY(30));
    UninstallRemoveData.Caption := '删除应用数据文件';
    UninstallRemoveData.Font.Size := 10;
    UninstallRemoveData.Checked := False;
    UninstallRemoveData.Enabled := Removed and SafeUninstallDataPath(Path);
    Detail := UninstallLabel(Options, '包括此安装目录内的设置、聊天记录和应用文件。删除后无法恢复。', 37, 49, 406, 55, 10);
    if not UninstallRemoveData.Enabled then begin
      if FileAttributes(Path) = $FFFFFFFF then Detail.Caption := '未找到可安全清理的应用数据。'
      else Detail.Caption := '此安装的数据目录现在无法安全清理。';
    end;

    UninstallCleanupStatus := TNewStaticText.Create(UninstallCompletion);
    UninstallCleanupStatus.Parent := UninstallCompletion;
    UninstallCleanupStatus.SetBounds(ScaleX(307), ScaleY(351), ScaleX(468), ScaleY(51));
    UninstallCleanupStatus.AutoSize := False;
    UninstallCleanupStatus.WordWrap := True;
    UninstallCleanupStatus.Font.Size := 10;
    if not HighContrastActive then UninstallCleanupStatus.Font.Color := $003E428E;
    Finish := TNewButton.Create(UninstallCompletion);
    Finish.Parent := UninstallCompletion;
    Finish.SetBounds(ScaleX(636), ScaleY(410), ScaleX(139), ScaleY(42));
    Finish.Caption := '完成';
    Finish.Default := True;
    Finish.OnClick := @FinishUninstallClick;
    UninstallCompletion.ActiveControl := Finish;
    UninstallCompletion.ShowModal;
  finally
    StopHeaderDrag;
    UninstallCompletion.Free;
  end;
end;

procedure DestroyUninstallWizard;
begin
  StopUninstallProgress(nil);
  if UninstallLogo <> nil then UninstallLogo.Free;
  if UninstallArt <> nil then UninstallArt.Free;
  if UninstallWorker <> 0 then CloseUninstallWorker(UninstallWorker);
end;
