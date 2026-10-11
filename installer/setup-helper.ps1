<#
  Допоміжний скрипт, який запускає інсталятор SchoolBell-Setup.exe (від імені адміністратора).
  Вручну запускати не потрібно.

  -Mode PreInstall    зупинити запущену програму перед копіюванням файлів
  -Mode PostInstall   папка даних, завдання Планувальника, живлення, Windows Update, запуск
  -Mode Uninstall     зупинити програму і видалити завдання Планувальника
#>
param(
    [Parameter(Mandatory)][ValidateSet('PreInstall', 'PostInstall', 'Uninstall')][string]$Mode,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'SchoolBell'),
    [switch]$NoPower,
    [switch]$NoUpdateHours
)

$ErrorActionPreference = 'Continue'
$TaskName = 'SchoolBell'
$UsersSid = 'S-1-5-32-545'   # BUILTIN\Users (SID, бо назва групи локалізована)

# Журнал пишемо у файл (UTF-8): вивід консолі інсталятор не показує коректно для кирилиці.
$LogDir = Join-Path $env:ProgramData 'SchoolBell\logs'
function Log($text) {
    try {
        New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
        $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  [$Mode] " + $text
        Add-Content -Path (Join-Path $LogDir 'install.log') -Value $line -Encoding UTF8
    }
    catch { }
}

function Stop-App {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        # Щоб «сторож» не запустив програму під час копіювання файлів.
        Disable-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Out-Null
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    $procs = Get-Process -Name 'SchoolBell' -ErrorAction SilentlyContinue
    if ($procs) {
        Log "Зупиняю запущену програму..."
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

switch ($Mode) {

    'PreInstall' {
        Stop-App
    }

    'Uninstall' {
        Stop-App
        if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
            Log "Завдання Планувальника видалено."
        }
    }

    'PostInstall' {
        $exe = Join-Path $InstallDir 'SchoolBell.exe'

        # ── Папка даних з правом запису для всіх користувачів ──
        $data = Join-Path $env:ProgramData 'SchoolBell'
        New-Item -ItemType Directory -Force -Path $data | Out-Null
        & icacls.exe $data /grant "*${UsersSid}:(OI)(CI)M" /T /C /Q | Out-Null
        Log "Папка даних: $data"

        # ── Завдання Планувальника ──
        $exeXml = [Security.SecurityElement]::Escape($exe)
        $dirXml = [Security.SecurityElement]::Escape($InstallDir)
        $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Шкільний дзвінок: запуск при вході та перезапуск, якщо програма не працює.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT20S</Delay>
      <Repetition>
        <Interval>PT5M</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>$UsersSid</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
    <RestartOnFailure>
      <Interval>PT1M</Interval>
      <Count>999</Count>
    </RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$exeXml</Command>
      <Arguments>--background</Arguments>
      <WorkingDirectory>$dirXml</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
        try {
            Register-ScheduledTask -TaskName $TaskName -Xml $taskXml -Force -ErrorAction Stop | Out-Null
            Enable-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue | Out-Null
            Log "Автозапуск налаштовано (Планувальник завдань: «$TaskName»)."
        }
        catch {
            Log "ПОМИЛКА: не вдалося створити завдання Планувальника: $($_.Exception.Message)"
            exit 2
        }

        # ── Живлення: без сну (екран може гаснути) ──
        if (-not $NoPower) {
            & powercfg.exe /change standby-timeout-ac 0 | Out-Null
            & powercfg.exe /change standby-timeout-dc 0 | Out-Null
            & powercfg.exe /change hibernate-timeout-ac 0 | Out-Null
            & powercfg.exe /change hibernate-timeout-dc 0 | Out-Null
            Log "Сон комп'ютера вимкнено."
        }

        # ── Windows Update: не перезавантажувати з 7:00 до 19:00 ──
        if (-not $NoUpdateHours) {
            try {
                $ux = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
                New-Item -Path $ux -Force -ErrorAction Stop | Out-Null
                Set-ItemProperty -Path $ux -Name 'SmartActiveHoursState' -Value 0 -Type DWord
                Set-ItemProperty -Path $ux -Name 'ActiveHoursStart' -Value 7 -Type DWord
                Set-ItemProperty -Path $ux -Name 'ActiveHoursEnd' -Value 19 -Type DWord
                Log "Активні години Windows Update: 7:00–19:00."
            }
            catch {
                Log "Не вдалося задати активні години Windows Update: $($_.Exception.Message)"
            }
        }

        # ── Синхронізація часу ──
        try {
            Set-Service -Name w32time -StartupType Automatic -ErrorAction Stop
            Start-Service -Name w32time -ErrorAction SilentlyContinue
        }
        catch { }

        # ── Запуск у сеансі користувача (не від імені адміністратора) ──
        try {
            Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
            Log "Програму запущено."
        }
        catch {
            Log "Програма запуститься при наступному вході в Windows."
        }
    }
}
exit 0
