function FileAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function OrdinaryPath(Path: String): Boolean;
var Root: String; Attributes: LongWord;
begin
  Root := ExpandConstant('{app}'); Result := False;
  while Length(Path) >= Length(Root) do begin
    Attributes := FileAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then Exit;
    if CompareText(Path, Root) = 0 then begin Result := True; Exit; end;
    Path := ExtractFileDir(Path);
  end;
end;

procedure RetireFile(Relative, Digest: String);
var Path, Backup: String;
begin
  Path := ExpandConstant('{app}\') + Relative;
  if not FileExists(Path) or not OrdinaryPath(Path) then Exit;
  try
    if CompareText(GetSHA256OfFile(Path), Digest) <> 0 then Exit;
  except
    Exit;
  end;
  Backup := ExpandConstant('{tmp}\retired\') + Relative;
  if not ForceDirectories(ExtractFileDir(Backup)) or not FileCopy(Path, Backup, False) then
    RaiseException('无法备份旧发行文件，已停止安装。');
  if not DeleteFile(Path) then RaiseException('无法移除旧发行文件，已停止安装。');
  RetiredPaths.Add(Relative);
end;

#include "RetiredFiles.iss"

procedure DeinitializeSetup;
var I: Integer; Path, Backup: String;
begin
  if RetiredPaths = nil then Exit;
  if not RetiredSuccess then
    for I := 0 to RetiredPaths.Count-1 do begin
      Path := ExpandConstant('{app}\') + RetiredPaths[I];
      Backup := ExpandConstant('{tmp}\retired\') + RetiredPaths[I];
      if not FileExists(Path) then begin
        ForceDirectories(ExtractFileDir(Path));
        if not FileCopy(Backup, Path, False) then
          Log('旧发行文件恢复失败：' + Path);
      end;
    end;
  RetiredPaths.Free;
end;
