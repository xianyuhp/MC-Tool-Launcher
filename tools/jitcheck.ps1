$ErrorActionPreference = 'Stop'
$exe = $args[0]

$asm = [Reflection.Assembly]::LoadFrom($exe)
"Loaded: $($asm.FullName)"

$targets = @(
    @('PCL.PageDownloadInstall', 'LoaderInit'),
    @('PCL.PageDownloadModify', 'LoadInstanceFinished'),
    @('PCL.PageDownloadResourceDetail', 'InitializeStackResourceVersions'),
    @('PCL.ModModpack', 'ModpackInstall'),
    @('PCL.PageDownloadResourceDetail', '_Lambda$__R18-5'),
    @('PCL.ModModpack+_Closure$__0-0', '_Lambda$__0')
)

$fail = 0
foreach ($t in $targets) {
    $type = $asm.GetType($t[0], $false)
    if ($null -eq $type) { "FAIL type not found: $($t[0])"; $fail++; continue }
    $found = $false
    foreach ($m in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')) {
        if ($m.Name -ne $t[1]) { continue }
        $found = $true
        $sig = "$($t[0])::$($m.Name)($(($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ','))"
        try {
            [Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($m.MethodHandle)
            "PASS JIT $sig"
        } catch {
            "FAIL JIT $sig : $($_.Exception.GetBaseException().Message)"
            $fail++
        }
    }
    if (-not $found) { "FAIL method not found: $($t[0])::$($t[1])"; $fail++ }
}
"failures=$fail"
exit $fail
