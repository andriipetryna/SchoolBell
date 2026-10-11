; ════════════════════════════════════════════════════════════════════
;  Інсталятор «Шкільний дзвінок» (NSIS 3)
;
;  Збирається скриптом scripts/build-installer.sh (macOS/Linux)
;  або scripts/build-installer.ps1 (Windows), які передають:
;     /DVERSION=1.0.0  /DPUBLISH_DIR=<папка з SchoolBell.exe>  /DOUT_FILE=<шлях до Setup.exe>
;
;  Тихе встановлення:  SchoolBell-Setup.exe /S
;  Тихе видалення:     "C:\Program Files\SchoolBell\Uninstall.exe" /S   (налаштування зберігаються)
; ════════════════════════════════════════════════════════════════════

Unicode true
ManifestDPIAware true
ManifestSupportedOS Win10
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !error "Передайте /DPUBLISH_DIR=<папка з SchoolBell.exe>"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "SchoolBell-Setup-${VERSION}.exe"
!endif

!define APP_NAME     "Шкільний дзвінок"
!define APP_EXE      "SchoolBell.exe"
!define APP_ID       "SchoolBell"
!define PUBLISHER    "SchoolBell"
!define UNINST_KEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define POWERSHELL   "$SYSDIR\WindowsPowerShell\v1.0\powershell.exe"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$PROGRAMFILES64\${APP_ID}"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=0 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=0 "FileDescription" "Інсталятор програми «${APP_NAME}»"
VIAddVersionKey /LANG=0 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=0 "LegalCopyright" "${PUBLISHER}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "Sections.nsh"
!include "FileFunc.nsh"

; ── Оформлення ──────────────────────────────────────────────────────
!define MUI_ICON   "..\src\SchoolBell.App\Assets\bell.ico"
!define MUI_UNICON "..\src\SchoolBell.App\Assets\bell.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP   "assets\wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "assets\wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "assets\header.bmp"
!define MUI_HEADERIMAGE_RIGHT
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_SMALLDESC

; ── Сторінки встановлення ───────────────────────────────────────────
!define MUI_WELCOMEPAGE_TITLE "Встановлення програми «${APP_NAME}»"
!define MUI_WELCOMEPAGE_TEXT "Програма за розкладом подає шкільні дзвінки, хвилину мовчання та сигнали повітряної тривоги й відбою.$\r$\n$\r$\nВона запускатиметься автоматично після входу в Windows і сама перезапускатиметься, якщо її закрили або стався збій.$\r$\n$\r$\nЯкщо програму вже встановлено, її буде оновлено. Налаштування і звуки збережуться.$\r$\n$\r$\nНатисніть «Далі», щоб продовжити."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "Встановлення завершено"
!define MUI_FINISHPAGE_TEXT "Програма «${APP_NAME}» встановлена і вже працює (іконка дзвіночка біля годинника).$\r$\n$\r$\nЩо зробити далі:$\r$\n  1. Відкрийте програму, оберіть звуки, розклад і громаду. Натисніть «Зберегти зміни».$\r$\n  2. Увімкніть автоматичний вхід у Windows (Sysinternals Autologon), щоб дзвінки працювали після перезавантаження без участі людини.$\r$\n  3. У BIOS увімкніть «Restore on AC Power Loss = Power On»."
!define MUI_FINISHPAGE_LINK "Завантажити Sysinternals Autologon"
!define MUI_FINISHPAGE_LINK_LOCATION "https://learn.microsoft.com/sysinternals/downloads/autologon"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT
!insertmacro MUI_PAGE_FINISH

; ── Сторінки видалення ──────────────────────────────────────────────
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Ukrainian"

; ── Перевірки перед стартом ─────────────────────────────────────────
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Програма потребує 64-розрядної Windows 10 або новішої."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Програма потребує Windows 10 або новішої."
    Abort
  ${EndIf}

  SetRegView 64
  SetShellVarContext all

  ; Оновлення: ставимо в ту саму папку, що й попередня версія.
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd

; Запуск setup-helper.ps1. Код виходу — у $0.
!macro RunHelper SCRIPT ARGS
  ${DisableX64FSRedirection}
  nsExec::Exec '"${POWERSHELL}" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${SCRIPT}" ${ARGS}'
  Pop $0
  ${EnableX64FSRedirection}
!macroend

; ── Секції ──────────────────────────────────────────────────────────
Section "Шкільний дзвінок (обов'язково)" SecMain
  SectionIn RO

  ; Тимчасова копія допоміжного скрипта.
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "setup-helper.ps1"

  DetailPrint "Зупинка попередньої версії (якщо запущена)..."
  !insertmacro RunHelper "$PLUGINSDIR\setup-helper.ps1" "-Mode PreInstall"

  DetailPrint "Копіювання файлів програми..."
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File /r "${PUBLISH_DIR}\*.*"
  File "setup-helper.ps1"
  File /oname=README.txt "..\README.md"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Меню «Пуск»
  CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  ; «Програми та компоненти»
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKLM "${UNINST_KEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
  WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "Ярлик на робочому столі" SecDesktop
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

Section "Вимкнути сон комп'ютера" SecPower
SectionEnd

Section "Без перезавантажень у шкільний час" SecUpdates
SectionEnd

; Остання (прихована) секція: автозапуск і системні налаштування.
Section "-PostInstall"
  StrCpy $1 ""
  ${IfNot} ${SectionIsSelected} ${SecPower}
    StrCpy $1 "$1 -NoPower"
  ${EndIf}
  ${IfNot} ${SectionIsSelected} ${SecUpdates}
    StrCpy $1 "$1 -NoUpdateHours"
  ${EndIf}

  DetailPrint "Налаштування автозапуску та запуск програми..."
  !insertmacro RunHelper "$INSTDIR\setup-helper.ps1" '-Mode PostInstall -InstallDir "$INSTDIR"$1'

  ${If} $0 != 0
    DetailPrint "Помилка налаштування автозапуску (код $0). Подробиці: $APPDATA\SchoolBell\logs\install.log"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Програму встановлено, але не вдалося налаштувати автозапуск (код $0).$\r$\n$\r$\nПодробиці: $APPDATA\SchoolBell\logs\install.log$\r$\n$\r$\nСпробуйте запустити інсталятор ще раз." /SD IDOK
  ${Else}
    DetailPrint "Автозапуск налаштовано, програму запущено."
  ${EndIf}
SectionEnd

; ── Описи компонентів ───────────────────────────────────────────────
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    "Програма, автозапуск після входу в Windows і перезапуск у разі збою (Планувальник завдань)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Ярлик «${APP_NAME}» на робочому столі для всіх користувачів."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecPower}   "Комп'ютер не засинатиме, тож дзвінки не пропускатимуться. Екран і далі може гаснути."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecUpdates} "Windows Update не перезавантажуватиме комп'ютер у шкільний час (активні години 7:00–19:00)."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ── Видалення ───────────────────────────────────────────────────────
Section "Uninstall"
  DetailPrint "Зупинка програми і видалення автозапуску..."
  ${If} ${FileExists} "$INSTDIR\setup-helper.ps1"
    !insertmacro RunHelper "$INSTDIR\setup-helper.ps1" "-Mode Uninstall"
  ${Else}
    nsExec::Exec 'taskkill /F /IM ${APP_EXE}'
    Pop $0
    nsExec::Exec 'schtasks /Delete /TN ${APP_ID} /F'
    Pop $0
  ${EndIf}
  Sleep 1000

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINST_KEY}"

  ; Налаштування і звуки — лише за згодою (у тихому режимі зберігаються).
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Видалити також налаштування розкладу та звукові файли?$\r$\n$\r$\n(Оберіть «Ні», якщо плануєте встановити програму знову.)" /SD IDNO IDNO keep_data
    RMDir /r "$APPDATA\SchoolBell"
    DetailPrint "Налаштування і звуки видалено."
  keep_data:
SectionEnd
