Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define PRODUCT "ComicEditor"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ComicEditor"
!define PROJECT_PROGID "ComicEditor.Cutscene"

!macro RegisterProjectExtension EXT
  ${If} ${Errors}
    SetErrorLevel 2
    Abort
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Classes\${EXT}" ""
  ; An unregistered extension is normal on first install. Do not carry the
  ; ReadRegStr missing-value flag into the write-error check below.
  ClearErrors
  WriteRegStr HKCU "Software\Classes\${EXT}\OpenWithProgids" "${PROJECT_PROGID}" ""
  ${If} $0 == ""
    WriteRegStr HKCU "Software\Classes\${EXT}" "" "${PROJECT_PROGID}"
  ${EndIf}
  WriteRegStr HKCU "Software\ComicEditor\Capabilities\FileAssociations" "${EXT}" "${PROJECT_PROGID}"
!macroend

!macro RemoveProjectExtension EXT
  ReadRegStr $0 HKCU "Software\Classes\${EXT}" ""
  ${If} $0 == "${PROJECT_PROGID}"
    DeleteRegValue HKCU "Software\Classes\${EXT}" ""
  ${EndIf}
  DeleteRegValue HKCU "Software\Classes\${EXT}\OpenWithProgids" "${PROJECT_PROGID}"
  DeleteRegKey /ifempty HKCU "Software\Classes\${EXT}\OpenWithProgids"
  DeleteRegKey /ifempty HKCU "Software\Classes\${EXT}"
!macroend

!macro RemoveProjectAssociations
  ReadRegStr $0 HKCU "Software\Classes\${PROJECT_PROGID}\shell\open\command" ""
  ${If} $0 == '$\"$INSTDIR\ComicEditor.Desktop.exe$\" $\"%1$\"'
    !insertmacro RemoveProjectExtension ".ctsc"
    !insertmacro RemoveProjectExtension ".cutscene"
    DeleteRegKey HKCU "Software\Classes\${PROJECT_PROGID}"
    DeleteRegValue HKCU "Software\RegisteredApplications" "ComicEditor"
    DeleteRegKey HKCU "Software\ComicEditor\Capabilities"
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
  ${EndIf}
!macroend
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
      !insertmacro RemoveProjectAssociations
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
    ; Releases through 0.1.7 accidentally shipped these debug symbols. Remove
    ; only those owned paths, including during an old updater's /STAGE install.
    ; No wildcard deletion: projects and unrelated developer files must survive.
    Delete "$INSTDIR\ComicEditor.Desktop.pdb"
    Delete "$INSTDIR\ComicEditor.pdb"
    Delete "$INSTDIR\ComicEditor.Format.pdb"
    Delete "$INSTDIR\libSkiaSharp.pdb"
    Delete "$INSTDIR\libHarfBuzzSharp.pdb"
    Delete "$INSTDIR\compiler\comic-compile.pdb"
    Delete "$INSTDIR\compiler\ComicEditor.pdb"
    Delete "$INSTDIR\compiler\ComicEditor.Format.pdb"
    Delete "$INSTDIR\compiler\libSkiaSharp.pdb"
    Delete "$INSTDIR\compiler\libHarfBuzzSharp.pdb"
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
    WriteRegStr HKCU "Software\Classes\${PROJECT_PROGID}" "" "ComicEditor Cutscene"
    WriteRegStr HKCU "Software\Classes\${PROJECT_PROGID}\DefaultIcon" "" '$\"$INSTDIR\comic-editor.ico$\"'
    WriteRegStr HKCU "Software\Classes\${PROJECT_PROGID}\shell\open\command" "" '$\"$INSTDIR\ComicEditor.Desktop.exe$\" $\"%1$\"'
    WriteRegStr HKCU "Software\ComicEditor\Capabilities" "ApplicationName" "ComicEditor"
    WriteRegStr HKCU "Software\ComicEditor\Capabilities" "ApplicationDescription" "Draw and localize game cutscenes"
    WriteRegStr HKCU "Software\RegisteredApplications" "ComicEditor" "Software\ComicEditor\Capabilities"
    !insertmacro RegisterProjectExtension ".ctsc"
    !insertmacro RegisterProjectExtension ".cutscene"
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
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
    !insertmacro RemoveProjectAssociations
    Delete "$SMPROGRAMS\ComicEditor.lnk"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
  ${EndIf}
  ; Exact package files only. Never recursively delete a user's project folder.
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
SectionEnd
