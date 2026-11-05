<#
.SYNOPSIS
    Offline reverse-engineering workflow wrapper for the GW2 custom-client repository.

.DESCRIPTION
    Repository-local generator and consumer for maintained artifacts only:
      * evidence import   | verify  - hashes and provenance for local, ignored captures and dumps

    It does not attach to a running process, does not scan an image, and does not implement an
    image-coordinate case. Offline PE/disassembly inspection belongs in IDA/MCP, and live
    tracing belongs in a debugger or an external inspector; see tools/README.md.

    Build-sensitive output records the game build it was produced against, so captured output stays
    tied to the binary it describes.

.EXAMPLE
    .\tools\gw2re.ps1 evidence import -Path .\captures\local\208784 -Build 208784 -BuildLabel 208784
.EXAMPLE
    .\tools\gw2re.ps1 evidence verify
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('evidence')]
    [string] $Area,

    [Parameter(Mandatory = $true, Position = 1)]
    [string] $Action,

    [string] $Path,
    [int] $Build = 0,
    [string] $BuildLabel,
    [string] $ImageSha256,
    [string] $Manifest,
    [string] $ProcessName,
    [string] $RuntimeBase,
    [string] $ImageSize,
    [string] $CapturedAtUtc,
    [string] $SourceNote,
    [string] $Out,
    [string] $RelativeTo,
    [string] $EntryNote,
    [string[]] $Tags = @(),
    [string[]] $Exclude,
    [string[]] $Include,
    [switch] $WireEligible,
    [switch] $Force,
    [switch] $Strict,
    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The operator wrapper must work both in Windows PowerShell 5.1 (no '??', no -AsHashtable) and in
# PowerShell 7, so it sticks to syntax both understand.

# A JSON array is a distinct type so an empty or one-element list cannot be confused with a
# scalar. PowerShell flattens an Object[] of length 1 to its single element when it crosses a
# pipeline or a return, which is how "messages": [ {...} ] previously round-tripped out as
# "messages": { ... }. Wrapping the list keeps the array shape explicit and unambiguous.
if (-not ('gw2re.JsonArray' -as [type])) {
    Add-Type -TypeDefinition @'
namespace gw2re {
    // Wraps a JSON array so its list shape survives a round-trip. Implements IEnumerable and an
    // indexer so existing code that does `foreach ($x in $arr)` or `$arr[0]` keeps working.
    public sealed class JsonArray : System.Collections.Generic.IEnumerable<object> {
        private readonly object[] _items;
        public JsonArray(object[] items) { _items = items ?? new object[0]; }
        public object[] Items { get { return _items; } }
        public int Count { get { return _items.Length; } }
        public object this[int index] { get { return _items[index]; } }
        public System.Collections.Generic.IEnumerator<object> GetEnumerator() {
            foreach (object item in _items) { yield return item; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return _items.GetEnumerator();
        }
    }

    // OrderedDictionary preserves insertion (on-disk) key order, but unlike Hashtable it has no
    // ContainsKey. The rest of this script calls ContainsKey throughout, so this subclass adds it
    // rather than rewriting every call site.
    public sealed class OrderedMap : System.Collections.Specialized.OrderedDictionary {
        public bool ContainsKey(object key) { return this.Contains(key); }
    }
}
'@
}

$script:ExitCode = 0
$script:Errors = [System.Collections.Generic.List[string]]::new()
$script:Warnings = [System.Collections.Generic.List[string]]::new()
$script:Infos = [System.Collections.Generic.List[string]]::new()

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:ToolVersion = 'gw2re/1'

# --- evidence accounting -------------------------------------------------------------

function Add-Error {
    param([Parameter(Mandatory = $true)][string] $Message)
    $script:Errors.Add($Message)
    $script:ExitCode = 1
}

function Add-Warning {
    param([Parameter(Mandatory = $true)][string] $Message)
    $script:Warnings.Add($Message)
}

function Add-Info {
    param([Parameter(Mandatory = $true)][string] $Message)
    $script:Infos.Add($Message)
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string] $Message,
        [Parameter(Mandatory = $true)][string] $Path
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Error "$Message :: missing file '$Path'"
        return $false
    }
    return $true
}

# --- path and hash helpers -----------------------------------------------------------

function Get-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Value)
    if ([System.IO.Path]::IsPathRooted($Value)) {
        return [System.IO.Path]::GetFullPath($Value)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $Value))
}

function Get-RelativeRepoPath {
    param(
        [Parameter(Mandatory = $true)][string] $FullPath,
        [string] $BasePath
    )
    $base = if ($BasePath) { $BasePath } else { $script:RepoRoot }
    $baseFull = [System.IO.Path]::GetFullPath($base)
    if (-not $baseFull.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $baseFull += [System.IO.Path]::DirectorySeparatorChar
    }
    $uri = [System.Uri]::new($baseFull)
    $target = [System.Uri]::new([System.IO.Path]::GetFullPath($FullPath))
    return [System.Uri]::UnescapeDataString($uri.MakeRelativeUri($target).ToString()).Replace('\', '/')
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string] $FilePath)
    return (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function ConvertTo-Hashtable {
    param($InputObject)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        # OrderedMap preserves the on-disk key order. A plain @{} enumerates in an
        # implementation-defined order, which reorders top-level keys such as schemaVersion
        # and template on every write and produces a large spurious diff.
        $result = [gw2re.OrderedMap]::new()
        foreach ($key in $InputObject.Keys) {
            $result[[string] $key] = ConvertTo-Hashtable -InputObject $InputObject[$key]
        }
        return $result
    }
    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $result = [gw2re.OrderedMap]::new()
        foreach ($property in $InputObject.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-Hashtable -InputObject $property.Value
        }
        return $result
    }
    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        # A JSON array must round-trip as an array even when it holds zero or one element.
        # Returning a bare Object[] here is ambiguous: the pipeline flattens it, so a
        # one-element list reaches Write-JsonValue as a scalar and is emitted as an object.
        # That turned "messages": [ {...} ] into "messages": { ... }, which is not an array
        # and violates the manifest schema. Wrap it so the list shape survives.
        $items = @($InputObject | ForEach-Object { ConvertTo-Hashtable -InputObject $_ })
        return , ([gw2re.JsonArray]::new($items))
    }
    return $InputObject
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string] $FilePath)
    $raw = Get-Content -LiteralPath $FilePath -Raw
    $parsed = $raw | ConvertFrom-Json
    $node = ConvertTo-Hashtable -InputObject $parsed
    if ($null -eq $node) { $node = [gw2re.OrderedMap]::new() }
    # ConvertFrom-Json yields $null for an empty array literal; restore the list shape explicitly.
    foreach ($listKey in @('buildStamps', 'messages', 'researchLeads', 'supportedWireBuilds', 'fields', 'sourceReferences', 'sessionPrerequisites', 'wireFixtures', 'unresolved', 'stages', 'entries', 'records', 'tags', 'kind')) {
        if ($node.ContainsKey($listKey) -and $null -eq $node[$listKey]) {
            $node[$listKey] = [gw2re.JsonArray]::new(@())
        }
    }
    return @{ Raw = $raw; Node = $node }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string] $FilePath,
        [Parameter(Mandatory = $true)][string] $Content
    )
    $directory = Split-Path -Parent $FilePath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($FilePath, $Content, $encoding)
}
function ConvertTo-StableJson {
    param([Parameter(Mandatory = $true)] $InputObject)

    # ConvertTo-Json renders an empty or one-element collection as a bare object, so it cannot be used
    # to write an evidence list that must round-trip as []. This writer emits arrays and objects
    # explicitly and always ends the file with a newline.
    $builder = [System.Text.StringBuilder]::new()
    [void] (Write-JsonValue -Builder $builder -Value $InputObject -Indent 0)
    [void] $builder.Append("`n")
    return ($builder.ToString() -replace "`r`n", "`n")
}

function Write-JsonValue {
    param(
        [Parameter(Mandatory = $true)][System.Text.StringBuilder] $Builder,
        $Value,
        [Parameter(Mandatory = $true)][int] $Indent
    )
    if ($null -eq $Value) {
        [void] $Builder.Append('null')
        return
    }
    if ($Value -is [bool]) {
        [void] $Builder.Append($(if ($Value) { 'true' } else { 'false' }))
        return
    }
    if ($Value -is [int] -or $Value -is [long]) {
        [void] $Builder.Append([string] $Value)
        return
    }
    if ($Value -is [double] -or $Value -is [decimal]) {
        [void] $Builder.Append(([double] $Value).ToString([System.Globalization.CultureInfo]::InvariantCulture))
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        Write-JsonObject -Builder $Builder -Map $Value -Indent $Indent
        return
    }
    if ($Value -is [gw2re.JsonArray]) {
        Write-JsonArray -Builder $Builder -Items @($Value.Items) -Indent $Indent
        return
    }
    if ($Value -is [string]) {
        [void] $Builder.Append((ConvertTo-JsonString -Text $Value))
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        Write-JsonArray -Builder $Builder -Items @($Value) -Indent $Indent
        return
    }
    [void] $Builder.Append((ConvertTo-JsonString -Text ([string] $Value)))
}

function Write-JsonObject {
    param(
        [Parameter(Mandatory = $true)][System.Text.StringBuilder] $Builder,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary] $Map,
        [Parameter(Mandatory = $true)][int] $Indent
    )
    $keys = @($Map.Keys)
    if ($keys.Count -eq 0) {
        [void] $Builder.Append('{}')
        return
    }
    $pad = ' ' * ($Indent + 4)
    $closePad = ' ' * $Indent
    [void] $Builder.Append("{`n")
    for ($index = 0; $index -lt $keys.Count; $index++) {
        $key = $keys[$index]
        [void] $Builder.Append($pad)
        [void] $Builder.Append((ConvertTo-JsonString -Text ([string] $key)))
        [void] $Builder.Append(': ')
        Write-JsonValue -Builder $Builder -Value $Map[$key] -Indent ($Indent + 4)
        if ($index -lt ($keys.Count - 1)) {
            [void] $Builder.Append(',')
        }
        [void] $Builder.Append("`n")
    }
    [void] $Builder.Append($closePad)
    [void] $Builder.Append('}')
}

function Write-JsonArray {
    param(
        [Parameter(Mandatory = $true)][System.Text.StringBuilder] $Builder,
        [object[]] $Items = @(),
        [Parameter(Mandatory = $true)][int] $Indent
    )
    if ($Items.Count -eq 0) {
        [void] $Builder.Append('[]')
        return
    }
    $pad = ' ' * ($Indent + 4)
    $closePad = ' ' * $Indent
    [void] $Builder.Append("[`n")
    for ($index = 0; $index -lt $Items.Count; $index++) {
        [void] $Builder.Append($pad)
        Write-JsonValue -Builder $Builder -Value $Items[$index] -Indent ($Indent + 4)
        if ($index -lt ($Items.Count - 1)) {
            [void] $Builder.Append(',')
        }
        [void] $Builder.Append("`n")
    }
    [void] $Builder.Append($closePad)
    [void] $Builder.Append(']')
}

function ConvertTo-JsonString {
    param([Parameter(Mandatory = $true)][string] $Text)
    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append('"')
    foreach ($character in $Text.ToCharArray()) {
        switch ($character) {
            '"' { [void] $builder.Append('\"') }
            '\' { [void] $builder.Append('\\') }
            "`b" { [void] $builder.Append('\b') }
            "`f" { [void] $builder.Append('\f') }
            "`n" { [void] $builder.Append('\n') }
            "`r" { [void] $builder.Append('\r') }
            "`t" { [void] $builder.Append('\t') }
            default {
                if ([int] $character -lt 0x20) {
                    [void] $builder.Append('\u' + ([int] $character).ToString('x4'))
                }
                else {
                    [void] $builder.Append($character)
                }
            }
        }
    }
    [void] $builder.Append('"')
    return $builder.ToString()
}

# --- evidence manifest ----------------------------------------------------------------

function Get-EvidenceKinds {
    param([Parameter(Mandatory = $true)][string] $FileName)
    $extension = [System.IO.Path]::GetExtension($FileName).ToLowerInvariant()
    $kinds = [System.Collections.Generic.List[string]]::new()
    $kinds.Add('unknown')
    if ($FileName -match '(?i)\.(?:pcap|pcapng)$') { $kinds.Add('wire-capture') }
    if ($FileName -match '(?i)(?:memdump|memory|dump)') { $kinds.Add('module-snapshot') }
    if ($FileName -match '(?i)(?:\^|[-_.])(?:request|sent|cmsg|outbound)(?:[-_.]|$)') { $kinds.Add('outbound-payload') }
    if ($FileName -match '(?i)(?:\^|[-_.])(?:response|recv|received|smsg|inbound)(?:[-_.]|$)') { $kinds.Add('inbound-payload') }
    if ($FileName -match '(?i)(?:^|[-_.])(?:asset|blob|raw)(?:[-_.]|$)' -and @('.png', '.jpg', '.jpeg', '.bmp', '.dds', '.wav', '.mp3', '.ogg', '.bin') -contains $extension) { $kinds.Add('asset') }
    if ($extension -eq '.sessionkey') { $kinds.Add('session-key') }
    if ($extension -eq '.json' -or $extension -eq '.txt') { $kinds.Add('record') }
    return @($kinds)
}

function Test-EvidenceBundleMetadata {
    param(
        [Parameter(Mandatory = $true)] $Bundle,
        [string] $Hash,
        [string] $Base
    )
    if ($Hash) {
        if ($Hash -notmatch '^[0-9a-f]{64}$') {
            Add-Error 'evidence import :: -ImageSha256 must be 64 lowercase hex characters'
            return $false
        }
        $Bundle['imageSha256'] = $Hash
    }
    if ($Base) {
        if ($Base -notmatch '^0x[0-9A-Fa-f]+$') {
            Add-Error 'evidence import :: -RuntimeBase must be hex, e.g. 0x7FF600000000'
            return $false
        }
        $Bundle['runtimeBase'] = $Base
    }
    return $true
}

function Get-EvidenceFileList {
    param(
        [Parameter(Mandatory = $true)][string] $SourceFull,
        [Parameter(Mandatory = $true)][string] $RelativeBase,
        [string[]] $ExcludeList
    )
    $files = [System.Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $SourceFull -PathType Leaf) {
        $files.Add((Get-Item -LiteralPath $SourceFull))
        return $files
    }
    $excludePatterns = @('artifacts/ida-exports/**', 'artifacts/manifests/**')
    if ($ExcludeList) { $excludePatterns = @($excludePatterns + $ExcludeList) }
    foreach ($candidate in @(Get-ChildItem -LiteralPath $SourceFull -Recurse -File)) {
        $relative = Get-RelativeRepoPath -FullPath $candidate.FullName -BasePath $RelativeBase
        $skip = $false
        foreach ($pattern in $excludePatterns) {
            if ($relative -like $pattern) { $skip = $true; break }
        }
        if (-not $skip) { $files.Add($candidate) }
    }
    return $files
}

function New-EvidenceEntry {
    param(
        [Parameter(Mandatory = $true)] $File,
        [Parameter(Mandatory = $true)][string] $RelativeBase,
        [string] $EntryNote,
        [switch] $WireEligible
    )
    $relative = Get-RelativeRepoPath -FullPath $File.FullName -BasePath $RelativeBase
    $extension = [System.IO.Path]::GetExtension($File.Name).ToLowerInvariant()
    if ($extension -eq '.sessionkey') {
        Add-Error "evidence import :: refusing to import '$relative'; session keys are never committed"
        return $null
    }
    if ($extension -eq '.dat' -and $File.Name -match '(?i)^Gw2\.dat$') {
        Add-Error "evidence import :: refusing to import '$relative'; the game archive is never committed"
        return $null
    }
    $kinds = @(Get-EvidenceKinds -FileName $File.Name)
    $entry = [ordered]@{
        kind     = [gw2re.JsonArray]::new($kinds)
        bytes    = $File.Length
        sha256   = Get-Sha256Hex -FilePath $File.FullName
        relative = $relative
        imported = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    if ($WireEligible) {
        if (@($kinds) -contains 'inbound-payload' -or @($kinds) -contains 'outbound-payload') {
            $entry['wireEligible'] = $true
        }
        else {
            Add-Error "evidence import :: -WireEligible cannot be applied to '$relative'; the representation is not a captured payload"
        }
    }
    if ($EntryNote) { $entry['note'] = $EntryNote }
    return $entry
}

function Invoke-EvidenceImport {
    param(
        [Parameter(Mandatory = $true)][string] $SourcePath,
        [Parameter(Mandatory = $true)][int] $BuildNumber,
        [string] $Label,
        [string] $Hash,
        [string] $Process,
        [string] $Base,
        [string] $Size,
        [string] $CapturedAt,
        [string[]] $TagList,
        [string[]] $ExcludeList,
        [string] $RelativeToPath,
        [string] $ManifestPath,
        [string] $EntryNote,
        [switch] $WireEligible
    )
    if ($BuildNumber -le 0) {
        Add-Error 'evidence import :: -Build is required and must be positive'
        return
    }
    $sourceFull = Get-RepoPath -Value $SourcePath
    if (-not (Test-Path -LiteralPath $sourceFull)) {
        Add-Error "evidence import :: source '$SourcePath' does not exist"
        return
    }
    $manifestFull = Get-RepoPath -Value $ManifestPath
    if (Test-Path -LiteralPath $manifestFull) {
        Add-Error "evidence import :: '$ManifestPath' already exists; choose another -Manifest path or delete it deliberately"
        return
    }
    # A single-file import is relative to its own directory so the recorded path stays repository-relative.
    $relativeBase = if ($RelativeToPath) { Get-RepoPath -Value $RelativeToPath }
    elseif (Test-Path -LiteralPath $sourceFull -PathType Leaf) { Split-Path -Parent $sourceFull }
    else { $sourceFull }

    $bundle = [ordered]@{
        schemaVersion  = 1
        bundle         = Split-Path -Leaf $sourceFull
        build          = $BuildNumber
        importedAtUtc  = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        tool           = $script:ToolVersion
        representation = 'local-private-input'
        classification = 'offline evidence; not wire compatibility'
        bundleRoot     = (Get-RelativeRepoPath -FullPath $relativeBase)
    }
    if ($Label) { $bundle['buildLabel'] = $Label }
    if (-not (Test-EvidenceBundleMetadata -Bundle $bundle -Hash $Hash -Base $Base)) { return }
    if ($Process) { $bundle['processName'] = $Process }
    if ($Size) { $bundle['imageSize'] = $Size }
    if ($CapturedAt) { $bundle['capturedAtUtc'] = $CapturedAt }
    if ($TagList) { $bundle['tags'] = [gw2re.JsonArray]::new(@($TagList)) }

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-EvidenceFileList -SourceFull $sourceFull -RelativeBase $relativeBase -ExcludeList $ExcludeList | Sort-Object FullName)) {
        $entry = New-EvidenceEntry -File $file -RelativeBase $relativeBase -EntryNote $EntryNote -WireEligible:$WireEligible
        if ($null -ne $entry) { $entries.Add($entry) }
    }
    if ($entries.Count -eq 0) {
        Add-Error "evidence import :: no importable files under '$SourcePath'"
        return
    }
    if ($script:Errors.Count -gt 0) {
        Add-Error 'evidence import :: refusing to write a manifest for an import that reported errors'
        return
    }
    $bundle['entries'] = [gw2re.JsonArray]::new(@($entries))

    Write-Utf8NoBom -FilePath $manifestFull -Content (ConvertTo-StableJson -InputObject $bundle)
    Add-Info "evidence import :: $($entries.Count) entries written to '$ManifestPath' for build $BuildNumber"
    foreach ($kind in @('session-key', 'asset')) {
        if (@($entries | Where-Object { @($_.kind) -contains $kind }).Count -gt 0) {
            Add-Warning "evidence import :: bundle contains '$kind' entries; keep it out of Git and pass -Exclude next time"
        }
    }
}

function Invoke-EvidenceVerify {
    param([Parameter(Mandatory = $true)][string] $ManifestPath)
    $manifestFull = Get-RepoPath -Value $ManifestPath
    if (-not (Invoke-Checked -Message 'evidence manifest' -Path $manifestFull)) { return }

    $manifest = (Read-JsonFile -FilePath $manifestFull).Node
    if (-not $manifest.ContainsKey('entries')) {
        Add-Error "evidence verify :: '$ManifestPath' has no entries"
        return
    }
    $base = Split-Path -Parent $manifestFull
    if ($manifest.ContainsKey('bundleRoot') -and $manifest['bundleRoot']) {
        $resolved = Get-RepoPath -Value ([string] $manifest['bundleRoot'])
        if (Test-Path -LiteralPath $resolved -PathType Container) { $base = $resolved }
        else { Add-Warning "evidence verify :: recorded bundleRoot '$($manifest['bundleRoot'])' is not present; resolving next to the manifest" }
    }
    $verified = 0
    $absent = 0
    foreach ($entry in @($manifest['entries'])) {
        $relative = [string] $entry['relative']
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $base ($relative -replace '/', '\')))
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Add-Warning "evidence verify :: '$relative' is not present next to the manifest"
            $absent++
            continue
        }
        $actual = Get-Sha256Hex -FilePath $candidate
        if ($actual -ne [string] $entry['sha256']) {
            Add-Error "evidence verify :: '$relative' sha256 mismatch (recorded $($entry['sha256']), actual $actual)"
            continue
        }
        $verified++
    }
    Add-Info "evidence verify :: $verified entries verified, $absent absent from the bundle directory"
}

function Invoke-EvidenceProvenance {
    param(
        [Parameter(Mandatory = $true)][string] $TargetPath,
        [string] $ManifestPath
    )
    $targetFull = Get-RepoPath -Value $TargetPath
    if (-not (Invoke-Checked -Message 'evidence path' -Path $targetFull)) { return }
    $targetHash = Get-Sha256Hex -FilePath $targetFull
    Add-Info "evidence provenance :: '$(Get-RelativeRepoPath -FullPath $targetFull)' sha256 $targetHash"

    $manifests = @()
    if ($ManifestPath) {
        $manifests = @(Get-RepoPath -Value $ManifestPath)
    }
    else {
        $root = Join-Path $script:RepoRoot 'artifacts/manifests'
        if (Test-Path -LiteralPath $root) {
            $manifests = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.json' | Select-Object -ExpandProperty FullName)
        }
    }
    if ($manifests.Count -eq 0) {
        Add-Warning 'evidence provenance :: no evidence manifests found; the input has no recorded provenance'
    }
    foreach ($manifestFile in $manifests) {
        try {
            $manifest = (Read-JsonFile -FilePath $manifestFile).Node
        }
        catch {
            Add-Warning "evidence provenance :: skipping unreadable manifest '$manifestFile'"
            continue
        }
        if (-not $manifest.ContainsKey('entries')) { continue }
        foreach ($entry in @($manifest['entries'])) {
            if ([string] $entry['sha256'] -eq $targetHash) {
                Add-Info "evidence provenance :: imported from bundle '$($manifest['bundle'])' for build $($manifest['build']) as '$($entry['relative'])' (kinds: $([string]::Join(', ', @($entry['kind']))))"
            }
        }
    }
}

# --- dispatch -------------------------------------------------------------------------


switch ("$Area $Action") {
    'evidence import' {
        if (-not $Path) {
            Add-Error 'evidence import :: -Path is required'
        }
        elseif (-not $Manifest) {
            Add-Error 'evidence import :: -Manifest is required (a bundle manifest under artifacts/manifests)'
        }
        else {
            Invoke-EvidenceImport -SourcePath $Path -BuildNumber $Build -Label $BuildLabel -Hash $ImageSha256 -Process $ProcessName -Base $RuntimeBase -Size $ImageSize -CapturedAt $CapturedAtUtc -TagList $Tags -ExcludeList $Exclude -RelativeToPath $RelativeTo -ManifestPath $Manifest -EntryNote $EntryNote -WireEligible:$WireEligible
        }
    }
    'evidence verify' {
        if (-not $Manifest) {
            Add-Error 'evidence verify :: -Manifest is required'
        }
        else {
            Invoke-EvidenceVerify -ManifestPath $Manifest
        }
    }
    'evidence provenance' {
        if (-not $Path) {
            Add-Error 'evidence provenance :: -Path is required'
        }
        else {
            Invoke-EvidenceProvenance -TargetPath $Path -ManifestPath $Manifest
        }
    }
    default {
        Add-Error "unknown command '$Area $Action'; see tools/README.md for the supported surface"
    }
}

# --- reporting ------------------------------------------------------------------------

$result = [ordered]@{
    tool    = $script:ToolVersion
    command = "$Area $Action"
    build   = if ($Build -gt 0) { $Build } else { $null }
    exit    = $script:ExitCode
    errors  = @($script:Errors)
    warnings = @($script:Warnings)
    infos   = @($script:Infos)
}

if ($Json) {
    # Structured output mode: one JSON object, so callers never scrape prose.
    Write-Output (ConvertTo-StableJson -InputObject $result)
}
else {
    foreach ($line in $script:Infos) { Write-Host "info    $line" }
    foreach ($line in $script:Warnings) { Write-Host "warning $line" -ForegroundColor Yellow }
    foreach ($line in $script:Errors) { Write-Host "error   $line" -ForegroundColor Red }
    $summary = if ($script:ExitCode -eq 0) { 'OK' } else { 'FAILED' }
    Write-Host "$summary $Area $Action :: $($script:Errors.Count) error(s), $($script:Warnings.Count) warning(s)"
}

if ($Strict -and $script:Warnings.Count -gt 0) {
    $script:ExitCode = 1
}

# Setting $LASTEXITCODE as well as exiting keeps the status visible to a caller that invokes the
# script from another script block or through powershell -Command.
$global:LASTEXITCODE = $script:ExitCode
exit $script:ExitCode
