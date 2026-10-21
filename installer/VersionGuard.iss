type
  TReleaseParts = array of Integer;

function ReleaseNumber(Value: String; var Number: Integer): Boolean;
var I: Integer;
begin
  Result := False;
  if (Value = '') or (Length(Value) > 10) then Exit;
  for I := 1 to Length(Value) do
    if (Value[I] < '0') or (Value[I] > '9') then Exit;
  Number := StrToIntDef(Value, -1);
  Result := Number >= 0;
end;

function ParseRelease(Value: String; var Parts: TReleaseParts): Boolean;
var P, I, Count, Number: Integer; Preview: Boolean; Tail, Token: String;
begin
  Result := False; SetArrayLength(Parts, 6);
  P := Pos('+', Value); if P > 0 then Value := Copy(Value, 1, P-1);
  P := Pos('-dev', Value); Preview := P > 0;
  Parts[4] := 1;
  if Preview then begin
    Parts[4] := 0; Tail := Copy(Value, P+4, Length(Value));
    if Tail <> '' then begin
      if Tail[1] <> '.' then Exit;
      Delete(Tail, 1, 1);
      if not ReleaseNumber(Tail, Number) then Exit;
      if Number < 1 then Exit;
      Parts[5] := Number;
    end;
    Value := Copy(Value, 1, P-1);
  end;
  Count := 0;
  repeat
    P := Pos('.', Value);
    if P = 0 then Token := Value else Token := Copy(Value, 1, P-1);
    if Count > 3 then Exit;
    if not ReleaseNumber(Token, Number) then Exit;
    if Number > 65534 then Exit;
    Parts[Count] := Number; Count := Count+1;
    if P = 0 then Break;
    Delete(Value, 1, P);
  until False;
  Result := (Count >= 3) and ((not Preview) or (Count = 3));
end;

function ReadInstalledVersion(Path: String; var Installed: String): Boolean;
var Shell, Folder, Item: Variant;
begin
  Installed := '';
  try
    Shell := CreateOleObject('Shell.Application');
    Folder := Shell.NameSpace(ExtractFileDir(Path));
    Item := Folder.ParseName(ExtractFileName(Path));
    Installed := Item.ExtendedProperty('System.Software.ProductVersion');
  except
    Installed := '';
  end;
  Result := Installed <> '';
  if not Result then Result := GetVersionNumbersString(Path, Installed);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Current, Target: TReleaseParts; Path, Installed, Expected: String; I: Integer;
begin
  Result := ''; Path := ExpandConstant('{app}\桌面伙伴.exe');
  Expected := ExpandConstant('{param:EXPECTEDBUILD|}');
  if Expected <> '' then begin
#ifdef InformationalVersion
    if Expected <> '{#InformationalVersion}' then begin
#else
    begin
#endif
      Result := '安装包与预期发行身份不一致，已停止更新。'; Exit;
    end;
  end;
  if not FileExists(Path) then Exit;
  if not ReadInstalledVersion(Path, Installed) then begin
      Result := '无法读取当前目录的程序版本，请选择新的安装目录。'; Exit;
    end;
  if not ParseRelease(Installed, Current) then begin
    Result := '当前目录的发行身份无效，请选择新的安装目录。'; Exit;
  end;
  if not ParseRelease('{#ProductVersion}', Target) then begin
    Result := '安装包版本无效。'; Exit;
  end;
  for I := 0 to 5 do begin
    if Target[I] > Current[I] then Exit;
    if Target[I] < Current[I] then begin
      Result := '禁止降级：当前已安装 ' + Installed + '。'; Exit;
    end;
  end;
end;
