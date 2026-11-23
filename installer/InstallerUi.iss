{ Native Inno pages retain their directory validation, disk-space checking,
  browse dialog, progress/status and cancellation. Artwork is presentation only. }
var
  HeroPanel, HeaderPanel: TPanel;
  HeroImage, HeaderLogo, SuccessImage: TBitmapImage;
  ProgressTrack: TBitmapImage;
  ProgressFill: TPanel;
  CompletionDivider: TBitmapImage;
  CompletionQuiet: TLabel;
  HeroName, UiTitle, UiLead, UiHint: TLabel;
  UiPercent: TNewStaticText;
  UiPercentSign, InstallTarget, StageStatus: TLabel;
  ResetDirectory: TNewButton;
  RoleDots: array[0..4] of TNewButton;
  RoleBitmaps: array[0..4] of TPngImage;
  CurrentRole: Integer;
  ProgressSpinner: TBitmapImage;
  SpinnerFrames: array[0..11] of TPngImage;
  SpinnerFrame: Integer;
  CarouselTimer: LongWord;
  UiClosing: Boolean;
  NativeSized: Boolean;
  CarouselElapsed: Integer;
  StepLabels: array[0..2] of TLabel;
  MinimizeButton, CloseButton: TNewButton;

#include "NativeStyle.iss"

function SetCarouselTimer(Window: HWND; Id: LongWord; Interval: Cardinal; Callback: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';
function KillCarouselTimer(Window: HWND; Id: LongWord): Boolean;
  external 'KillTimer@user32.dll stdcall';

function UiLabel(Parent: TWinControl; Caption: String; X, Y, Width, Height, Size: Integer): TLabel;
begin
  Result := TLabel.Create(WizardForm);
  Result.Parent := Parent;
  Result.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(Width), ScaleY(Height));
  Result.AutoSize := False; Result.WordWrap := True;
  Result.Transparent := True;
  Result.Caption := Caption; Result.Font.Height := -ScaleY(Round(Size * 4.0 / 3.0));
  Result.Font.Color := $00492D20;
end;

procedure SelectRole(Index: Integer);
var I, X, Width: Integer;
begin
  CurrentRole := Index;
  HeroImage.PngImage.Assign(RoleBitmaps[Index]);
  case Index of
    0: HeroName.Caption := 'E包';
    1: HeroName.Caption := '大肥鱼';
    2: HeroName.Caption := '马尔芙';
    3: HeroName.Caption := '小贝';
    4: HeroName.Caption := '咕嘎';
  end;
  X := 134;
  for I := 0 to 4 do begin
    Width := 7; if I = Index then Width := 23;
    RoleDots[I].Left := ScaleX(X + (Width-26) div 2); X := X + Width + 10;
  end;
  RefreshStyledButtons;
end;

procedure RoleClick(Sender: TObject);
begin
  CarouselElapsed := 0;
  SelectRole(TNewButton(Sender).Tag);
end;

procedure CarouselTick(Window: HWND; Msg: Cardinal; Id: LongWord; Time: Cardinal);
begin
  if UiClosing or (CarouselTimer = 0) then Exit;
  { Do not animate through modal path / cancellation / error dialogs. }
  RefreshStyledButtons;
  if WizardForm.Enabled then begin
    if (WizardForm.CurPageID = wpInstalling) and (ProgressSpinner <> nil) then begin
      SpinnerFrame := (SpinnerFrame + 1) mod 12;
      ProgressSpinner.PngImage.Assign(SpinnerFrames[SpinnerFrame]);
    end;
    CarouselElapsed := CarouselElapsed + 100;
    if CarouselElapsed >= 4500 then begin SelectRole((CurrentRole + 1) mod 5); CarouselElapsed := 0; end;
  end;
end;

procedure StopInstallerUi;
var I: Integer;
begin
  UiClosing := True;
  if CarouselTimer <> 0 then begin
    KillCarouselTimer(0, CarouselTimer); CarouselTimer := 0;
  end;
  for I := 0 to 4 do
    if RoleBitmaps[I] <> nil then RoleBitmaps[I].Free;
  for I := 0 to 11 do
    if SpinnerFrames[I] <> nil then SpinnerFrames[I].Free;
  StopNativeStyle;
end;

procedure MinimizeClick(Sender: TObject);
begin
  StyleShowWindow(WizardForm.Handle, 6);
end;

procedure CloseClick(Sender: TObject);
begin
  if WizardForm.CurPageID = wpFinished then FinishLaterClick(Sender)
  else WizardForm.CancelButton.OnClick(WizardForm.CancelButton);
end;

procedure RoundInstallerWindow(Sender: TObject);
begin
  StyleSetRegion(WizardForm.Handle, StyleRegion(0, 0, WizardForm.Width, WizardForm.Height, ScaleX(24), ScaleY(24)), True);
end;

procedure ResetDirectoryClick(Sender: TObject);
begin
  WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\EbaoDeskPet');
  WizardForm.ActiveControl := WizardForm.DirEdit;
end;

procedure InitializeInstallerUi;
var I: Integer; Name: String; LabelControl: TLabel; PathBorder, InfoBox, StageIcon: TBitmapImage;
begin
  if WizardSilent then Exit;
  LaunchOnFinish := True;
  WizardForm.BorderStyle := bsNone;
  WizardForm.ClientWidth := ScaleX(1000);
  WizardForm.ClientHeight := ScaleY(636);
  WizardForm.Color := clWhite;
  WizardForm.Font.Name := 'Microsoft YaHei UI';
  WizardForm.MainPanel.Visible := False;
  WizardForm.Bevel.Visible := False;
  WizardForm.Bevel1.Visible := False;
  WizardForm.OuterNotebook.SetBounds(ScaleX(400), ScaleY(224), ScaleX(560), ScaleY(312));
  WizardForm.InnerNotebook.SetBounds(0, 0, ScaleX(560), ScaleY(312));
  WizardForm.OuterNotebook.Anchors := [akLeft, akTop, akRight, akBottom];
  WizardForm.InnerNotebook.Anchors := [akLeft, akTop, akRight, akBottom];

  HeaderPanel := TPanel.Create(WizardForm); HeaderPanel.Parent := WizardForm;
  HeaderPanel.SetBounds(0, 0, WizardForm.ClientWidth, ScaleY(54));
  HeaderPanel.Anchors := [akLeft, akTop, akRight]; HeaderPanel.BevelOuter := bvNone; HeaderPanel.ParentBackground := False; HeaderPanel.Color := clWhite;
  ExtractTemporaryFile('deskpet-logo.png');
  HeaderLogo := TBitmapImage.Create(WizardForm); HeaderLogo.Parent := HeaderPanel;
  HeaderLogo.SetBounds(ScaleX(22), ScaleY(11), ScaleX(30), ScaleY(30)); HeaderLogo.Stretch := True;
  HeaderLogo.PngImage.LoadFromFile(ExpandConstant('{tmp}\deskpet-logo.png'));
  HeaderLogo.BackColor := clWhite;
  LabelControl := UiLabel(HeaderPanel, '桌面伙伴 · 安装向导', 63, 17, 205, 24, 10);
  LabelControl.Font.Style := [fsBold];
  LabelControl := UiLabel(HeaderPanel, '{#ProductVersion}', 198, 19, 260, 24, 8); LabelControl.Font.Color := $00A59085;
  StageIcon := TBitmapImage.Create(WizardForm); StageIcon.Parent := WizardForm;
  StageIcon.SetBounds(0, ScaleY(54), WizardForm.ClientWidth, ScaleY(1)); StageIcon.Stretch := True;
  ExtractTemporaryFile('divider.png'); StageIcon.PngImage.LoadFromFile(ExpandConstant('{tmp}\divider.png'));
  MinimizeButton := TNewButton.Create(WizardForm); MinimizeButton.Parent := HeaderPanel;
  MinimizeButton.SetBounds(ScaleX(908), ScaleY(8), ScaleX(38), ScaleY(38)); MinimizeButton.Caption := '−'; MinimizeButton.OnClick := @MinimizeClick; StyleButton(MinimizeButton, 4);
  CloseButton := TNewButton.Create(WizardForm); CloseButton.Parent := HeaderPanel;
  CloseButton.SetBounds(ScaleX(950), ScaleY(8), ScaleX(38), ScaleY(38)); CloseButton.Caption := '×'; CloseButton.OnClick := @CloseClick; StyleButton(CloseButton, 4);

  HeroPanel := TPanel.Create(WizardForm); HeroPanel.Parent := WizardForm;
  HeroPanel.SetBounds(0, ScaleY(54), ScaleX(360), WizardForm.ClientHeight - ScaleY(54));
  HeroPanel.Anchors := [akLeft, akTop, akBottom]; HeroPanel.BevelOuter := bvNone; HeroPanel.ParentBackground := False; HeroPanel.Color := $00FFF4ED;
  LabelControl := UiLabel(HeroPanel, '让陪伴，来到桌面', 24, 28, 312, 32, 16); LabelControl.Alignment := taCenter; LabelControl.Font.Style := [fsBold];
  LabelControl := UiLabel(HeroPanel, '五位伙伴，等你认识', 24, 66, 312, 24, 9); LabelControl.Alignment := taCenter; LabelControl.Font.Color := $00AB8876;
  HeroImage := TBitmapImage.Create(WizardForm); HeroImage.Parent := HeroPanel;
  HeroImage.SetBounds(0, 0, ScaleX(360), ScaleY(582));
  HeroImage.Stretch := True; HeroImage.BackColor := HeroPanel.Color;
  HeroImage.SendToBack;
  for I := 0 to 4 do begin
    case I of 0: Name := 'e-bao'; 1: Name := 'dafeiyu'; 2: Name := 'marfu'; 3: Name := 'xiaobei'; 4: Name := 'gugugaga'; end;
    ExtractTemporaryFile(Name + '-panel.png');
    RoleBitmaps[I] := TPngImage.Create;
    RoleBitmaps[I].LoadFromFile(ExpandConstant('{tmp}\') + Name + '-panel.png');
  end;
  HeroName := UiLabel(HeroPanel, '', 24, 452, 312, 30, 14); HeroName.Alignment := taCenter; HeroName.Font.Style := [fsBold];
  for I := 0 to 4 do begin
    RoleDots[I] := TNewButton.Create(WizardForm); RoleDots[I].Parent := HeroPanel;
    RoleDots[I].SetBounds(ScaleX(107+I*30), ScaleY(486), ScaleX(26), ScaleY(26));
    RoleDots[I].Tag := I; RoleDots[I].OnClick := @RoleClick;
    case I of 0: Name := 'E包'; 1: Name := '大肥鱼'; 2: Name := '马尔芙'; 3: Name := '小贝'; 4: Name := '咕嘎'; end;
    RoleDots[I].Caption := Name; RoleDots[I].Hint := '切换至' + Name; RoleDots[I].ShowHint := True; StyleButton(RoleDots[I], 2);
  end;
  SelectRole(0);

  StepLabels[0] := UiLabel(WizardForm, '选择位置', 400, 88, 70, 23, 9);
  LabelControl := UiLabel(WizardForm, '────', 459, 88, 35, 23, 9); LabelControl.Font.Color := $00EFE3DC;
  StepLabels[1] := UiLabel(WizardForm, '安装', 504, 88, 30, 23, 9);
  LabelControl := UiLabel(WizardForm, '────', 538, 88, 35, 23, 9); LabelControl.Font.Color := $00EFE3DC;
  StepLabels[2] := UiLabel(WizardForm, '完成', 583, 88, 40, 23, 9);
  UiTitle := UiLabel(WizardForm, '', 400, 130, 560, 40, 20); UiTitle.Font.Style := [fsBold];
  UiLead := UiLabel(WizardForm, '', 400, 177, 560, 28, 10); UiLead.Font.Color := $0090786C;
  WizardForm.SelectDirLabel.Visible := False; WizardForm.SelectDirBrowseLabel.Visible := False;
  WizardForm.SelectDirBitmapImage.Visible := False;
  LabelControl := UiLabel(WizardForm.SelectDirPage, '安装位置', 0, 6, 320, 24, 10); LabelControl.Font.Style := [fsBold];
  ResetDirectory := TNewButton.Create(WizardForm); ResetDirectory.Parent := WizardForm.SelectDirPage;
  ResetDirectory.SetBounds(ScaleX(450), 0, ScaleX(110), ScaleY(30)); ResetDirectory.Caption := '恢复默认'; ResetDirectory.OnClick := @ResetDirectoryClick; StyleButton(ResetDirectory, 3);
  ExtractTemporaryFile('path-field.png');
  PathBorder := TBitmapImage.Create(WizardForm); PathBorder.Parent := WizardForm.SelectDirPage;
  PathBorder.SetBounds(0, ScaleY(36), ScaleX(439), ScaleY(45)); PathBorder.Stretch := True; PathBorder.PngImage.LoadFromFile(ExpandConstant('{tmp}\path-field.png'));
  WizardForm.DirEdit.BorderStyle := bsNone; WizardForm.DirEdit.Color := $00FFFCFA;
  WizardForm.DirEdit.Font.Height := -ScaleY(13);
  WizardForm.DirEdit.SetBounds(ScaleX(12), ScaleY(49), ScaleX(415), ScaleY(24));
  WizardForm.DirBrowseButton.SetBounds(ScaleX(449), ScaleY(36), ScaleX(110), ScaleY(45)); WizardForm.DirBrowseButton.Caption := '浏览文件夹…'; StyleButton(WizardForm.DirBrowseButton, 0);
  LabelControl := UiLabel(WizardForm.SelectDirPage, '可直接输入或粘贴路径，也可以浏览文件夹选择。', 0, 90, 560, 30, 8); LabelControl.Font.Color := $00A89081;
  ExtractTemporaryFile('info-box.png');
  InfoBox := TBitmapImage.Create(WizardForm); InfoBox.Parent := WizardForm.SelectDirPage;
  InfoBox.SetBounds(0, ScaleY(132), ScaleX(560), ScaleY(45)); InfoBox.Stretch := True; InfoBox.PngImage.LoadFromFile(ExpandConstant('{tmp}\info-box.png'));
  LabelControl := UiLabel(WizardForm.SelectDirPage, '更新会保留你的设置、聊天记录和已选择的伙伴。', 44, 146, 500, 30, 9); LabelControl.Font.Color := $009C8070;
  WizardForm.DiskSpaceLabel.Parent := WizardForm;
  WizardForm.DiskSpaceLabel.SetBounds(ScaleX(400), ScaleY(578), ScaleX(410), ScaleY(20)); WizardForm.DiskSpaceLabel.Font.Size := 8; WizardForm.DiskSpaceLabel.Font.Color := $00AA968B;
  WizardForm.StatusLabel.SetBounds(0, ScaleY(14), ScaleX(430), ScaleY(36)); WizardForm.StatusLabel.Font.Size := 10; WizardForm.StatusLabel.Font.Color := $00492D20;
  WizardForm.FilenameLabel.Visible := False;
  LabelControl := UiLabel(WizardForm.InstallingPage, '正在解压并写入文件…', 0, 87, 560, 28, 9); LabelControl.Font.Color := $00A89081;
  WizardForm.ProgressGauge.Visible := False;
  ExtractTemporaryFile('progress-track.png');
  ProgressTrack := TBitmapImage.Create(WizardForm); ProgressTrack.Parent := WizardForm.InstallingPage;
  ProgressTrack.SetBounds(0, ScaleY(63), ScaleX(560), ScaleY(9)); ProgressTrack.Stretch := True;
  ProgressTrack.PngImage.LoadFromFile(ExpandConstant('{tmp}\progress-track.png')); ProgressTrack.BackColor := clWhite;
  ProgressFill := TPanel.Create(WizardForm); ProgressFill.Parent := WizardForm.InstallingPage;
  ProgressFill.SetBounds(0, ScaleY(63), 1, ScaleY(9)); ProgressFill.ParentBackground := False;
  ProgressFill.BevelOuter := bvNone; ProgressFill.Color := $00ED6845; ProgressFill.Visible := False;
  UiPercent := TNewStaticText.Create(WizardForm); UiPercent.Parent := WizardForm.InstallingPage;
  UiPercent.AutoSize := True; UiPercent.SetBounds(ScaleX(454), 0, ScaleX(86), ScaleY(52)); UiPercent.Caption := '0'; UiPercent.Font.Height := -ScaleY(35); UiPercent.Font.Color := $00ED6845; UiPercent.Color := clWhite;
  UiPercentSign := UiLabel(WizardForm.InstallingPage, '%', 542, 19, 18, 30, 13); UiPercentSign.Font.Color := $00ED6845;
  LabelControl := UiLabel(WizardForm.InstallingPage, '检查安装环境', 26, 130, 534, 28, 10); LabelControl.Font.Color := $00918070;
  StageStatus := UiLabel(WizardForm.InstallingPage, '已完成', 440, 130, 120, 28, 8); StageStatus.Alignment := taRightJustify; StageStatus.Font.Color := $00B29B8D;
  LabelControl := UiLabel(WizardForm.InstallingPage, '安装程序与角色资源', 26, 169, 534, 28, 10); LabelControl.Font.Color := $00704931;
  StageStatus := UiLabel(WizardForm.InstallingPage, '进行中', 440, 169, 120, 28, 8); StageStatus.Alignment := taRightJustify; StageStatus.Font.Color := $00B29B8D;
  LabelControl := UiLabel(WizardForm.InstallingPage, '完成安装校验', 26, 208, 534, 28, 10); LabelControl.Font.Color := $00B5A49A;
  for I := 0 to 2 do begin
    Name := 'stage-' + IntToStr(I); ExtractTemporaryFile(Name + '.png');
    StageIcon := TBitmapImage.Create(WizardForm); StageIcon.Parent := WizardForm.InstallingPage;
    StageIcon.SetBounds(0, ScaleY(130+I*39), ScaleX(19), ScaleY(19)); StageIcon.Stretch := True;
    StageIcon.PngImage.LoadFromFile(ExpandConstant('{tmp}\') + Name + '.png'); StageIcon.BackColor := clWhite;
    if I = 1 then ProgressSpinner := StageIcon;
  end;
  for I := 0 to 11 do begin
    Name := 'stage-spinner-' + IntToStr(I); ExtractTemporaryFile(Name + '.png');
    SpinnerFrames[I] := TPngImage.Create;
    SpinnerFrames[I].LoadFromFile(ExpandConstant('{tmp}\') + Name + '.png');
  end;
  UiHint := UiLabel(WizardForm.InstallingPage, '更新后将继续使用你上次选择的伙伴。', 0, 264, 548, 36, 9); UiHint.Font.Color := $00A48C7C;

  WizardForm.FinishedHeadingLabel.Visible := False; WizardForm.FinishedLabel.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False; WizardForm.RunList.Visible := False;
  ExtractTemporaryFile('success.png');
  SuccessImage := TBitmapImage.Create(WizardForm); SuccessImage.Parent := WizardForm;
  SuccessImage.SetBounds(ScaleX(400), ScaleY(130), ScaleX(56), ScaleY(56)); SuccessImage.Stretch := True; SuccessImage.Visible := False;
  SuccessImage.PngImage.LoadFromFile(ExpandConstant('{tmp}\success.png'));
  SuccessImage.BackColor := clWhite;
  LabelControl := UiLabel(WizardForm.FinishedPage, '按你的习惯，做两个小设置', 0, 0, 548, 24, 9); LabelControl.Font.Color := $0090786C;
  FinishDesktop := TNewCheckBox.Create(WizardForm); FinishDesktop.Parent := WizardForm.FinishedPage;
  FinishDesktop.SetBounds(0, ScaleY(35), ScaleX(548), ScaleY(24)); FinishDesktop.Caption := '  创建桌面快捷方式';
  FinishDesktop.Font.Size := 10; FinishDesktop.Font.Color := $00492D20;
  FinishDesktop.Font.Height := -ScaleY(13);
  FinishStartup := TNewCheckBox.Create(WizardForm); FinishStartup.Parent := WizardForm.FinishedPage;
  FinishStartup.SetBounds(0, ScaleY(72), ScaleX(548), ScaleY(24)); FinishStartup.Caption := '  开机时启动桌面伙伴';
  FinishStartup.Font.Size := 10; FinishStartup.Font.Color := $00492D20;
  FinishStartup.Font.Height := -ScaleY(13);
  FinishNotice := UiLabel(WizardForm.FinishedPage, '', 0, 108, 548, 28, 8); FinishNotice.Font.Color := $00A89081;
  CompletionQuiet := UiLabel(WizardForm, '更新后，将继续使用你上次选择的伙伴。', 400, 479, 558, 25, 9); CompletionQuiet.Font.Color := $00A48C7C; CompletionQuiet.Visible := False;
  InstallTarget := UiLabel(WizardForm, '', 400, 578, 400, 25, 8); InstallTarget.Font.Color := $00AA968B; InstallTarget.Visible := False;
  InstallTarget.WordWrap := False; InstallTarget.ShowHint := True;
  CompletionDivider := TBitmapImage.Create(WizardForm); CompletionDivider.Parent := WizardForm;
  CompletionDivider.SetBounds(ScaleX(400), ScaleY(301), ScaleX(558), ScaleY(1)); CompletionDivider.Stretch := True;
  ExtractTemporaryFile('divider.png'); CompletionDivider.PngImage.LoadFromFile(ExpandConstant('{tmp}\divider.png')); CompletionDivider.Visible := False;
  FinishLater := TNewButton.Create(WizardForm); FinishLater.Parent := WizardForm;
  FinishLater.SetBounds(ScaleX(400), ScaleY(562), ScaleX(124), ScaleY(46)); FinishLater.Caption := '完成，稍后启动'; FinishLater.OnClick := @FinishLaterClick; FinishLater.Visible := False; StyleButton(FinishLater, 3);
  WizardForm.NextButton.SetBounds(ScaleX(849), ScaleY(562), ScaleX(110), ScaleY(46)); StyleButton(WizardForm.NextButton, 1);
  WizardForm.CancelButton.SetBounds(ScaleX(849), ScaleY(562), ScaleX(110), ScaleY(46)); WizardForm.CancelButton.Caption := '取消安装'; StyleButton(WizardForm.CancelButton, 0);
  WizardForm.BackButton.Visible := False;
  CarouselTimer := SetCarouselTimer(0, 0, 100, CreateCallback(@CarouselTick));
  InitializeHeaderDrag;
  WizardForm.OnResize := @RoundInstallerWindow; RoundInstallerWindow(WizardForm);
end;

procedure CurPageChanged(CurPageID: Integer);
var I, ActiveStep: Integer;
begin
  if WizardSilent then Exit;
  { Ownership reads can pump COM messages. Do not publish editable choices or
    completion actions until every initial option has been loaded. }
  if (CurPageID = wpFinished) and not OptionsLoaded then begin
    WizardForm.NextButton.Enabled := False; FinishLater.Enabled := False;
    FinishDesktop.Enabled := False; FinishStartup.Enabled := False;
  end;
  { Inno performs its own final wizard sizing after InitializeWizard. }
  if not NativeSized then begin
    WizardForm.ClientWidth := ScaleX(1000); WizardForm.ClientHeight := ScaleY(636);
    NativeSized := True;
  end;
  WizardForm.BackButton.Visible := False;
  FinishLater.Visible := CurPageID = wpFinished;
  CompletionQuiet.Visible := CurPageID = wpFinished; CompletionDivider.Visible := CurPageID = wpFinished;
  InstallTarget.Visible := CurPageID = wpInstalling;
  InstallTarget.Caption := '安装至 ' + MinimizePathName(WizardDirValue, InstallTarget.Font, ScaleX(350));
  InstallTarget.Hint := WizardDirValue;
  WizardForm.CancelButton.Visible := CurPageID <> wpFinished;
  if CurPageID = wpInstalling then WizardForm.CancelButton.Left := ScaleX(849)
  else WizardForm.CancelButton.Left := -ScaleX(500);
  WizardForm.DiskSpaceLabel.Visible := CurPageID = wpSelectDir;
  ActiveStep := 1;
  if CurPageID = wpSelectDir then ActiveStep := 0;
  if CurPageID = wpFinished then ActiveStep := 2;
  for I := 0 to 2 do begin
    StepLabels[I].Font.Color := $00A8968C; StepLabels[I].Font.Style := [];
    if I = ActiveStep then begin StepLabels[I].Font.Color := $00ED6845; StepLabels[I].Font.Style := [fsBold]; end;
    if I < ActiveStep then StepLabels[I].Font.Color := $00748242;
  end;
  SuccessImage.Visible := CurPageID = wpFinished;
  UiTitle.Top := ScaleY(130); UiLead.Top := ScaleY(177); WizardForm.OuterNotebook.Top := ScaleY(224);
  WizardForm.OuterNotebook.Height := ScaleY(312); WizardForm.InnerNotebook.Height := ScaleY(312);
  if CurPageID = wpSelectDir then begin
    UiTitle.Caption := '为伙伴选个位置'; UiLead.Caption := '选择安装文件夹，准备好后就可以开始了。';
    WizardForm.NextButton.Caption := '开始安装';
  end else if CurPageID = wpFinished then begin
    UiTitle.Top := ScaleY(203); UiLead.Top := ScaleY(250); WizardForm.OuterNotebook.Top := ScaleY(324);
    WizardForm.OuterNotebook.Height := ScaleY(138); WizardForm.InnerNotebook.Height := ScaleY(138);
    WizardForm.NextButton.SetBounds(ScaleX(835), ScaleY(562), ScaleX(124), ScaleY(46));
    UiTitle.Caption := '安装完成，欢迎回家'; UiLead.Caption := '桌面伙伴已准备就绪，现在可以开始陪伴了。';
    WizardForm.NextButton.Caption := '完成并启动'; LoadFinishOptions;
    if OptionsLoaded and not OptionsLoading then begin
      WizardForm.NextButton.Enabled := True; FinishLater.Enabled := True;
    end;
  end else begin
    UiTitle.Caption := '伙伴正在安家'; UiLead.Caption := '稍等片刻，马上就能见面。';
  end;
  RefreshStyledButtons;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if WizardSilent or (MaxProgress <= 0) then Exit;
  WizardForm.ProgressGauge.Visible := False;
  ProgressFill.Width := ScaleX(Round((CurProgress * 560.0) / MaxProgress));
  ProgressFill.Visible := ProgressFill.Width > 0;
  StyleSetRegion(ProgressFill.Handle, StyleRegion(0, 0, ProgressFill.Width, ProgressFill.Height, ScaleX(9), ScaleY(9)), True);
  ProgressFill.BringToFront;
  UiPercent.Caption := IntToStr(Round((CurProgress * 100.0) / MaxProgress));
  UiPercent.Left := ScaleX(538) - UiPercent.Width;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpWelcome) or (PageID = wpSelectTasks) or (PageID = wpReady);
end;
