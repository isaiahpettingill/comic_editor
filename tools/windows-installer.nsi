Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define PRODUCT "ComicEditor"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ComicEditor"
Name "${PRODUCT}"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\ComicEditor"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
CRCCheck force
ManifestDPIAware true
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "ComicEditor"
VIAddVersionKey "FileDescription" "ComicEditor Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "0BSD"
!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\ComicEditor.Desktop.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var StageOnly
Var RegisterOnly
Var UnregisterOnly
Var DisplayVersion

!macro CheckAppClosed
  ${If} ${FileExists} "$INSTDIR\ComicEditor.Desktop.exe"
    System::Call 'kernel32::CreateFileW(w "$INSTDIR\ComicEditor.Desktop.exe", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p .r0'
    ${If} $0 == -1
      MessageBox MB_OK|MB_ICONSTOP "Close ComicEditor before installing or uninstalling it." /SD IDOK
      SetErrorLevel 2
      Abort
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r0)'
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  SetRegView 32
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "ComicEditor requires 64-bit Windows." /SD IDOK
    SetErrorLevel 2
    Quit
  ${EndIf}
  StrCpy $DisplayVersion "${VERSION}"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UNREGISTER" $1
  ${IfNot} ${Errors}
    StrCpy $UnregisterOnly 1
    SetSilent silent
  ${EndIf}
  ClearErrors
  ${GetOptions} $0 "/STAGE" $1
  ${IfNot} ${Errors}
    StrCpy $StageOnly 1
    SetSilent silent
  ${EndIf}
  ClearErrors
  ${GetOptions} $0 "/REGISTER" $1
  ${IfNot} ${Errors}
    StrCpy $RegisterOnly 1
    SetSilent silent
    ClearErrors
    ${GetOptions} $0 "/VERSION=" $1
    ${IfNot} ${Errors}
      StrCpy $DisplayVersion $1
    ${EndIf}
  ${EndIf}
FunctionEnd

Section "ComicEditor"
  ${If} $UnregisterOnly == 1
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $0 == $INSTDIR
      Delete "$SMPROGRAMS\ComicEditor.lnk"
      DeleteRegKey HKCU "${UNINSTALL_KEY}"
    ${EndIf}
    SetErrorLevel 0
    Return
  ${EndIf}
  SetOutPath "$INSTDIR"
  ${If} $RegisterOnly != 1
    !insertmacro CheckAppClosed
    ClearErrors
    SetOverwrite on
    !include "${INSTALL_FILES}"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    ${If} ${Errors}
      MessageBox MB_OK|MB_ICONSTOP "Installation failed. Close ComicEditor and try again." /SD IDOK
      SetErrorLevel 2
      Abort
    ${EndIf}
  ${EndIf}
  ${If} $StageOnly != 1
    IfFileExists "$INSTDIR\ComicEditor.Desktop.exe" +3
      SetErrorLevel 2
      Abort
    ClearErrors
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "ComicEditor"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "$DisplayVersion"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Isaiah Pettingill"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\comic-editor.ico"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/isaiahpettingill/comic_editor"
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${SIZE_KB}
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    SetOutPath "$INSTDIR"
    CreateShortcut "$SMPROGRAMS\ComicEditor.lnk" "$INSTDIR\ComicEditor.Desktop.exe" "" "$INSTDIR\comic-editor.ico"
    ${If} ${Errors}
      SetErrorLevel 2
      Abort
    ${EndIf}
  ${EndIf}
  SetErrorLevel 0
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 32
FunctionEnd

Section "Uninstall"
  !insertmacro CheckAppClosed
  ; Remove shell integration only if it still belongs to this installation.
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $0 == $INSTDIR
    Delete "$SMPROGRAMS\ComicEditor.lnk"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
  ${EndIf}
  ; Exact package files only. Never recursively delete a user's project folder.
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
SectionEnd
