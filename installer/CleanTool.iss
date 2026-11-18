; CleanTool installer (Inno Setup 7, or 6.3 or newer, https://jrsoftware.org).
;
; Per-user install: copies CleanTool.bundle to %AppData%\Autodesk\ApplicationPlugins, where AutoCAD loads it at
; startup. No administrator rights are needed. Adds an uninstaller to Windows' "Installed apps".
;
; Normally built by the Release build (src\CleanTool.AcadTool\CleanTool.AcadTool.csproj, target PackCleanTool),
; which passes the version and the staged bundle folder:
;   ISCC.exe /DAppVersion=1.0.0 /DBundleDir="...\artifacts\stage\CleanTool.bundle" CleanTool.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef BundleDir
  #define BundleDir "..\artifacts\stage\CleanTool.bundle"
#endif

[Setup]
; Keep this AppId for every version: it is how a new version finds and replaces the old one.
AppId={{0B49A998-F934-491A-85F3-A00F088EC177}
AppName=CleanTool for AutoCAD
AppVersion={#AppVersion}
AppVerName=CleanTool {#AppVersion}
AppPublisher=SonerAyar
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\CleanTool.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=CleanTool-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto
UninstallDisplayName=CleanTool {#AppVersion} for AutoCAD
VersionInfoVersion={#AppVersion}
; MIT license, shown on a license page (also installed into the bundle folder from the staged bundle).
LicenseFile=..\LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.AutoCADRunning=AutoCAD is running. Close AutoCAD and click Retry (AutoCAD locks the CleanTool files while it is open).
turkish.AutoCADRunning=AutoCAD açık. AutoCAD'i kapatıp Yeniden Dene'ye tıklayın (AutoCAD açıkken CleanTool dosyalarını kilitler).
english.Finished=CleanTool is installed. Start AutoCAD 2025 or 2026: it loads CleanTool automatically. Commands: TTC (cleanup), TTW (cut walls at columns).
turkish.Finished=CleanTool kuruldu. AutoCAD 2025 veya 2026'yı başlatın, CleanTool otomatik yüklenir. Komutlar: TTC (temizlik), TTW (duvarları kolonlarda kes).

[InstallDelete]
; Files of an older version that the new one no longer ships.
Type: filesandordirs; Name: "{app}\Contents"

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
function IsAutoCADRunning(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''acad.exe''');
    Result := Processes.Count > 0;
  except
    Result := False; { WMI not available: do not block the setup }
  end;
end;

{ Asks to close AutoCAD until it is closed or the user cancels. Silent setups (/SUPPRESSMSGBOXES) cancel. }
function WaitForAutoCADClosed(): Boolean;
begin
  Result := True;
  while IsAutoCADRunning() do
  begin
    if SuppressibleMsgBox(CustomMessage('AutoCADRunning'), mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := WaitForAutoCADClosed();
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForAutoCADClosed();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssDone) and not WizardSilent() then
    MsgBox(CustomMessage('Finished'), mbInformation, MB_OK);
end;
