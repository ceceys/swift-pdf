; SWIFT PDF kurulum paketi (Inno Setup 6)
; Önce: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
;        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
; Derleme: ISCC.exe setup\SwiftPdf.iss   (GitHub'da: .github\workflows\build.yml, /DAppVersion=<csproj Version>)
;
; Program kendini bu paketle günceller (Core\Updater.cs): /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS.
; Sessiz kurulum bitince program [Run]'daki WizardSilent satırıyla yeniden açılır.

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#define AppName "SWIFT PDF"
#define AppPublisher "Cuma Ali Dirik"
#define AppExe "SwiftPdf.exe"

[Setup]
AppId={{4F2B8C61-9D3E-4A7B-8E15-6C0D2A9F3B47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SWIFT PDF
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Hep kullanıcıya kurulur (yönetici istemez). "Herkes için" seçeneği yok: sessiz güncelleme başka klasöre ikinci
; bir kopya kurmasın.
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=SwiftPdf-Setup-{#AppVersion}
SetupIconFile=..\Assets\swiftpdf.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
#ifexist "compiler:Languages\Turkish.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Program içinden yapılan (sessiz) güncellemeden sonra programı yeniden aç
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\SwiftPdf\WebView2"
Type: files; Name: "{localappdata}\SwiftPdf\guncelleme.json"
Type: dirifempty; Name: "{localappdata}\SwiftPdf"
