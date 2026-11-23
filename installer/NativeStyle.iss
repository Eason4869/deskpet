{ Nonzero-alpha native buttons preserve hit testing, keyboard and accessibility.
  Their themed appearance is drawn below them with deterministic artwork. }
type
  TStyledButton = record
    Button: TNewButton;
    Surface: TBitmapImage;
    LabelControl: TLabel;
    Kind, LastState: Integer;
  end;
var
  StyledButtons: array of TStyledButton;
  PrimarySurface, SecondarySurface, LinkSurface, DotSurface, ActiveDotSurface,
    FocusPrimary, FocusSecondary, FocusLink, FocusDot: TPngImage;
  HeaderPreviousProc: LongWord;

function StyleGetLong(Window: HWND; Index: Integer): LongWord; external 'GetWindowLongW@user32.dll stdcall';
function StyleSetLong(Window: HWND; Index: Integer; Value: LongWord): LongWord; external 'SetWindowLongW@user32.dll stdcall';
function StyleAlpha(Window: HWND; Key: LongWord; Alpha: Byte; Flags: LongWord): Boolean; external 'SetLayeredWindowAttributes@user32.dll stdcall';
function StyleGetFocus: HWND; external 'GetFocus@user32.dll stdcall';
function StyleRegion(Left, Top, Right, Bottom, RX, RY: Integer): LongWord; external 'CreateRoundRectRgn@gdi32.dll stdcall';
function StyleSetRegion(Window: HWND; Region: LongWord; Redraw: Boolean): Integer; external 'SetWindowRgn@user32.dll stdcall';
function StyleReleaseCapture: Boolean; external 'ReleaseCapture@user32.dll stdcall';
function StyleShowWindow(Window: HWND; Command: Integer): Boolean; external 'ShowWindow@user32.dll stdcall';
function StyleCallProc(Previous: LongWord; Window: HWND; Msg, WParam, LParam: LongWord): Longint; external 'CallWindowProcW@user32.dll stdcall';
function StyleIsWindow(Window: HWND): Boolean; external 'IsWindow@user32.dll stdcall';

function ReadSurface(Name: String): TPngImage;
begin
  ExtractTemporaryFile(Name + '.png'); Result := TPngImage.Create;
  Result.LoadFromFile(ExpandConstant('{tmp}\') + Name + '.png');
end;

procedure RefreshStyledButtons;
var I, State: Integer; Image: TPngImage;
begin
  if WizardForm.CurPageID = wpInstalling then WizardForm.CancelButton.Caption := '取消安装';
  for I := 0 to GetArrayLength(StyledButtons)-1 do begin
    StyledButtons[I].Surface.SetBounds(StyledButtons[I].Button.Left, StyledButtons[I].Button.Top, StyledButtons[I].Button.Width, StyledButtons[I].Button.Height);
    StyledButtons[I].LabelControl.SetBounds(StyledButtons[I].Button.Left, StyledButtons[I].Button.Top + (StyledButtons[I].Button.Height - ScaleY(20)) div 2, StyledButtons[I].Button.Width, ScaleY(22));
    StyledButtons[I].Surface.Visible := StyledButtons[I].Button.Visible;
    StyledButtons[I].LabelControl.Visible := StyledButtons[I].Button.Visible and (StyledButtons[I].Kind <> 2);
    StyledButtons[I].LabelControl.Caption := StyledButtons[I].Button.Caption;
    StyledButtons[I].LabelControl.Enabled := StyledButtons[I].Button.Enabled;
    State := 0;
    if StyleGetFocus = StyledButtons[I].Button.Handle then State := 1;
    if StyledButtons[I].Kind = 2 then begin
      Image := DotSurface;
      if StyledButtons[I].Button.Tag = CurrentRole then Image := ActiveDotSurface;
      if State = 1 then Image := FocusDot;
      if State = 0 then begin
        if StyledButtons[I].Button.Tag = CurrentRole then
          StyledButtons[I].Surface.SetBounds(StyledButtons[I].Button.Left + ScaleX(1), StyledButtons[I].Button.Top + ScaleY(10), ScaleX(23), ScaleY(7))
        else
          StyledButtons[I].Surface.SetBounds(StyledButtons[I].Button.Left + ScaleX(9), StyledButtons[I].Button.Top + ScaleY(10), ScaleX(7), ScaleY(7));
      end;
    end else if StyledButtons[I].Kind = 1 then begin
      Image := PrimarySurface; if State = 1 then Image := FocusPrimary;
    end else if (StyledButtons[I].Kind = 3) or (StyledButtons[I].Kind = 4) then begin
      Image := LinkSurface; if State = 1 then Image := FocusLink;
    end else begin
      Image := SecondarySurface; if State = 1 then Image := FocusSecondary;
    end;
    if StyledButtons[I].Kind = 2 then State := State + (CurrentRole+1)*10;
    if State <> StyledButtons[I].LastState then begin
      StyledButtons[I].Surface.PngImage.Assign(Image); StyledButtons[I].LastState := State;
    end;
    StyledButtons[I].LabelControl.BringToFront;
    if (StyleGetLong(StyledButtons[I].Button.Handle, -20) and $80000) = 0 then begin
      StyleSetLong(StyledButtons[I].Button.Handle, -20, StyleGetLong(StyledButtons[I].Button.Handle, -20) or $80000);
      StyleAlpha(StyledButtons[I].Button.Handle, 0, 1, 2);
    end;
  end;
end;

procedure SurfaceClick(Sender: TObject);
var Index: Integer;
begin
  Index := TBitmapImage(Sender).Tag;
  if StyledButtons[Index].Button.Enabled then
    StyledButtons[Index].Button.OnClick(StyledButtons[Index].Button);
end;

procedure StyleButton(Button: TNewButton; Kind: Integer);
var Count: Integer;
begin
  if PrimarySurface = nil then begin
    PrimarySurface := ReadSurface('button-primary'); SecondarySurface := ReadSurface('button-secondary');
    LinkSurface := ReadSurface('button-link'); DotSurface := ReadSurface('dot'); ActiveDotSurface := ReadSurface('dot-active');
    FocusPrimary := ReadSurface('button-primary-focus'); FocusSecondary := ReadSurface('button-secondary-focus');
    FocusLink := ReadSurface('button-link-focus'); FocusDot := ReadSurface('dot-focus');
  end;
  Count := GetArrayLength(StyledButtons); SetArrayLength(StyledButtons, Count+1);
  StyledButtons[Count].Button := Button; StyledButtons[Count].Kind := Kind; StyledButtons[Count].LastState := -1;
  StyledButtons[Count].Surface := TBitmapImage.Create(WizardForm); StyledButtons[Count].Surface.Parent := Button.Parent;
  StyledButtons[Count].Surface.SetBounds(Button.Left, Button.Top, Button.Width, Button.Height);
  StyledButtons[Count].Surface.Stretch := True; StyledButtons[Count].Surface.Tag := Count;
  StyledButtons[Count].Surface.OnClick := @SurfaceClick;
  StyledButtons[Count].LabelControl := TLabel.Create(WizardForm); StyledButtons[Count].LabelControl.Parent := Button.Parent;
  StyledButtons[Count].LabelControl.SetBounds(Button.Left, Button.Top + (Button.Height - ScaleY(20)) div 2, Button.Width, ScaleY(22));
  StyledButtons[Count].LabelControl.AutoSize := False; StyledButtons[Count].LabelControl.Alignment := taCenter;
  StyledButtons[Count].LabelControl.Transparent := True; StyledButtons[Count].LabelControl.Font.Size := 10;
  StyledButtons[Count].LabelControl.Font.Height := -ScaleY(13);
  StyledButtons[Count].LabelControl.Font.Color := $00774D36;
  if Kind = 1 then StyledButtons[Count].LabelControl.Font.Color := clWhite;
  if Kind = 3 then StyledButtons[Count].LabelControl.Font.Color := $00ED6845;
  if Kind = 4 then StyledButtons[Count].LabelControl.Font.Color := $0095847A;
  RefreshStyledButtons;
end;

function HeaderDragProc(Window: HWND; Msg, WParam, LParam: LongWord): Longint;
begin
  if Msg = $201 then begin
    StyleReleaseCapture; SendMessage(WizardForm.Handle, $A1, 2, 0); Result := 0;
  end else Result := StyleCallProc(HeaderPreviousProc, Window, Msg, WParam, LParam);
end;

procedure InitializeHeaderDrag;
begin
  HeaderPreviousProc := StyleSetLong(HeaderPanel.Handle, -4, CreateCallback(@HeaderDragProc));
end;

procedure StopNativeStyle;
begin
  if (HeaderPreviousProc <> 0) and StyleIsWindow(HeaderPanel.Handle) then
    StyleSetLong(HeaderPanel.Handle, -4, HeaderPreviousProc);
  if PrimarySurface <> nil then begin
    PrimarySurface.Free; SecondarySurface.Free; LinkSurface.Free; DotSurface.Free; ActiveDotSurface.Free;
    FocusPrimary.Free; FocusSecondary.Free; FocusLink.Free; FocusDot.Free;
  end;
end;
