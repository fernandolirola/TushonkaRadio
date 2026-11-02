param(
    [string]$GamePath = 'C:\SPT',
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GamePath 'BepInEx/core/Mono.Cecil.dll')
$build = Join-Path $PSScriptRoot '../Client/bin/Release/net472'
$plugin = 'zezinho.HideoutRadio'
$files = @(Get-ChildItem -LiteralPath $build -Filter '*.dll')
# Unity provides the tuple types in mscorlib but omits the forwarding DLL.
$assets = Get-Content (Join-Path $PSScriptRoot '../Client/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$tuple = $assets.libraries.Keys | Where-Object { $_ -like 'System.ValueTuple/*' } | Select-Object -First 1
$facade = foreach ($folder in $assets.packageFolders.Keys) {
    $candidate = Join-Path $folder ($assets.libraries[$tuple].path + '/lib/net47/System.ValueTuple.dll')
    if (Test-Path -LiteralPath $candidate) { Get-Item -LiteralPath $candidate; break }
}
if (!$facade) { throw 'System.ValueTuple facade missing from the restored packages.' }
$files += $facade
$names = @{}
$versions = @{}
foreach ($file in $files) {
    if ($file.BaseName -notin $plugin, 'TagLibSharp') {
        $names[$file.BaseName] = 'TushonkaRadio.Private.' + $file.BaseName
        $versions[$file.BaseName] = [Reflection.AssemblyName]::GetAssemblyName($file.FullName).Version
    }
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($file in $files) {
    if ($file.BaseName -eq 'TagLibSharp') {
        Copy-Item -LiteralPath $file.FullName -Destination $Destination
        continue
    }
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName)
    try {
        foreach ($reference in $assembly.MainModule.AssemblyReferences) {
            if ($names.ContainsKey($reference.Name)) {
                $reference.Version = $versions[$reference.Name]
                $reference.Name = $names[$reference.Name]
                $reference.PublicKeyToken = [byte[]]@()
                $reference.PublicKey = [byte[]]@()
                $reference.HasPublicKey = $false
            }
        }
        if ($names.ContainsKey($file.BaseName)) {
            $assembly.Name.Name = $names[$file.BaseName]
            $assembly.Name.PublicKeyToken = [byte[]]@()
            $assembly.Name.PublicKey = [byte[]]@()
            $assembly.Name.HasPublicKey = $false
            $assembly.MainModule.Attributes = $assembly.MainModule.Attributes -band (-bnot [Mono.Cecil.ModuleAttributes]::StrongNameSigned)
        }
        if ($file.BaseName -eq 'YoutubeExplode') {
            # The bundled polyfills include a timer whose local ValueTask does
            # not satisfy the shared ITimer interface's external ValueTask.
            $types = $assembly.MainModule.GetTypes()
            $timers = $types | Where-Object Name -eq 'SystemTimeProviderTimer'
            $external = $timers.Methods | Where-Object { $_.Name -eq 'DisposeAsync' -and $_.ReturnType.Scope.Name -like '*Tasks.Extensions' } | Select-Object -First 1
            foreach ($timer in $timers) {
                $local = $timer.Methods | Where-Object { $_.Name -eq 'DisposeAsync' -and $_.ReturnType.Scope -is [Mono.Cecil.ModuleDefinition] }
                if (!$local) { continue }
                $dispose = $timer.Methods | Where-Object Name -eq 'Dispose' | Select-Object -First 1
                $method = [Mono.Cecil.MethodDefinition]::new('DisposeAsync', $external.Attributes, $external.ReturnType)
                $contract = [Mono.Cecil.MethodReference]::new('DisposeAsync', $external.ReturnType, ($timers.Interfaces.InterfaceType | Where-Object { $_.FullName -eq 'System.IAsyncDisposable' -and $_.Scope -is [Mono.Cecil.AssemblyNameReference] } | Select-Object -First 1))
                $contract.HasThis = $true
                $method.Overrides.Add($contract)
                $method.Name = 'System.IAsyncDisposable.DisposeAsync'
                $timer.Methods.Add($method)
                $body = $method.Body
                $result = [Mono.Cecil.Cil.VariableDefinition]::new($external.ReturnType)
                $body.Variables.Add($result)
                $il = $body.GetILProcessor()
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Ldarg_0)
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Call, $dispose)
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Ldloca_S, $result)
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Initobj, $external.ReturnType)
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Ldloc_0)
                $il.Emit([Mono.Cecil.Cil.OpCodes]::Ret)
            }
        }
        $assembly.Write((Join-Path $Destination ($assembly.Name.Name + '.dll')))
    }
    finally { $assembly.Dispose() }
}



