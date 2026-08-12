param(
    [string]$SourceDirectory = "assets\item-icons",
    [string]$ModDataDirectory = "src\JmServer.GameIntegration\ModData"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $SourceDirectory))
$modDataRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $ModDataDirectory))
$spriteRoot = Join-Path $modDataRoot "data\hd\global\ui\items\misc\jm_selectors"
$definitionRoot = Join-Path $modDataRoot "data\hd\items\misc\jm_selectors"
$workRoot = Join-Path $sourceRoot ".work"

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Selector icon source directory was not found: $sourceRoot"
}

$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
New-Item -ItemType Directory -Path $spriteRoot -Force | Out-Null
New-Item -ItemType Directory -Path $definitionRoot -Force | Out-Null
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

$definitions = @(
    # Unique selectors keep the antique-gold treatment.
    @{ Asset = "unique_sword"; Source = "selector-unique-sword.png"; Style = "native" },
    @{ Asset = "unique_axe"; Source = "selector-unique-axe.png"; Style = "native" },
    @{ Asset = "unique_blunt"; Source = "selector-unique-blunt.png"; Style = "native" },
    @{ Asset = "unique_bow"; Source = "selector-unique-bow.png"; Style = "native" },
    @{ Asset = "unique_pole"; Source = "selector-unique-pole.png"; Style = "native" },
    @{ Asset = "unique_dagger"; Source = "selector-unique-dagger.png"; Style = "native" },
    @{ Asset = "unique_caster"; Source = "selector-unique-caster.png"; Style = "native" },
    @{ Asset = "unique_class_weapon"; Source = "selector-unique-class-weapon.png"; Style = "native" },
    @{ Asset = "unique_body"; Source = "selector-unique-body.png"; Style = "native" },
    @{ Asset = "unique_helm"; Source = "selector-unique-helm.png"; Style = "native" },
    @{ Asset = "unique_shield"; Source = "selector-unique-shield.png"; Style = "native" },
    @{ Asset = "unique_gloves"; Source = "selector-unique-gloves.png"; Style = "native" },
    @{ Asset = "unique_boots"; Source = "selector-unique-boots.png"; Style = "native" },
    @{ Asset = "unique_belts"; Source = "selector-unique-belts.png"; Style = "native" },
    @{ Asset = "unique_class_armor"; Source = "selector-unique-class-armor.png"; Style = "native" },
    @{ Asset = "unique_rings"; Source = "selector-unique-rings.png"; Style = "native" },
    @{ Asset = "unique_amulets"; Source = "selector-unique-amulets.png"; Style = "native" },
    @{ Asset = "unique_charms"; Source = "selector-unique-charms.png"; Style = "native" },
    @{ Asset = "unique_other"; Source = "selector-unique-other.png"; Style = "native" },

    # Set selectors are green-shifted so their quality is visible before reading the name.
    @{ Asset = "set_weapons"; Source = "selector-set-weapons.png"; Style = "native" },
    @{ Asset = "set_body"; Source = "selector-set-body.png"; Style = "native" },
    @{ Asset = "set_helm"; Source = "selector-set-helm.png"; Style = "native" },
    @{ Asset = "set_shield"; Source = "selector-set-shield.png"; Style = "native" },
    @{ Asset = "set_gloves"; Source = "selector-set-gloves.png"; Style = "native" },
    @{ Asset = "set_boots"; Source = "selector-set-boots.png"; Style = "native" },
    @{ Asset = "set_belts"; Source = "selector-set-belts.png"; Style = "native" },
    @{ Asset = "set_class_armor"; Source = "selector-set-class-armor.png"; Style = "native" },
    @{ Asset = "set_jewelry"; Source = "selector-set-jewelry.png"; Style = "native" },
    @{ Asset = "set_other"; Source = "selector-set-other.png"; Style = "native" },

    # Base selectors use a silver treatment, distinct from unique and set selectors.
    @{ Asset = "base_sword"; Source = "selector-base-sword.png"; Style = "native" },
    @{ Asset = "base_axe"; Source = "selector-base-axe.png"; Style = "native" },
    @{ Asset = "base_blunt"; Source = "selector-base-blunt.png"; Style = "native" },
    @{ Asset = "base_bow"; Source = "selector-base-bow.png"; Style = "native" },
    @{ Asset = "base_pole"; Source = "selector-base-pole.png"; Style = "native" },
    @{ Asset = "base_dagger"; Source = "selector-base-dagger.png"; Style = "native" },
    @{ Asset = "base_caster"; Source = "selector-base-caster.png"; Style = "native" },
    @{ Asset = "base_class_weapon"; Source = "selector-base-class-weapon.png"; Style = "native" },
    @{ Asset = "base_body"; Source = "selector-base-body.png"; Style = "native" },
    @{ Asset = "base_helm"; Source = "selector-base-helm.png"; Style = "native" },
    @{ Asset = "base_shield"; Source = "selector-base-shield.png"; Style = "native" },
    @{ Asset = "base_gloves"; Source = "selector-base-gloves.png"; Style = "native" },
    @{ Asset = "base_boots"; Source = "selector-base-boots.png"; Style = "native" },
    @{ Asset = "base_belts"; Source = "selector-base-belts.png"; Style = "native" },
    @{ Asset = "base_class_armor"; Source = "selector-base-class-armor.png"; Style = "native" },

    @{ Asset = "materials_runes"; Source = "selector-materials-runes.png"; Style = "native" },
    @{ Asset = "materials_gems"; Source = "selector-materials-gems.png"; Style = "native" },
    @{ Asset = "materials_charms"; Source = "selector-materials-charms.png"; Style = "native" },

    @{ Asset = "skill_charms_amazon"; Source = "selector-skill-charms-amazon.png"; Style = "native" },
    @{ Asset = "skill_charms_sorceress"; Source = "selector-skill-charms-sorceress.png"; Style = "native" },
    @{ Asset = "skill_charms_necromancer"; Source = "selector-skill-charms-necromancer.png"; Style = "native" },
    @{ Asset = "skill_charms_paladin"; Source = "selector-skill-charms-paladin.png"; Style = "native" },
    @{ Asset = "skill_charms_barbarian"; Source = "selector-skill-charms-barbarian.png"; Style = "native" },
    @{ Asset = "skill_charms_druid"; Source = "selector-skill-charms-druid.png"; Style = "native" },
    @{ Asset = "skill_charms_assassin"; Source = "selector-skill-charms-assassin.png"; Style = "native" },
    @{ Asset = "skill_charms_warlock"; Source = "selector-skill-charms-warlock.png"; Style = "native" },
    @{ Asset = "popular_small_charms"; Source = "selector-popular-small-charms.png"; Style = "native" },
    @{ Asset = "popular_large_charms"; Source = "selector-popular-large-charms.png"; Style = "native" },

    @{ Asset = "base_mode_normal"; Source = "selector-base-mode-normal.png"; Style = "native" },
    @{ Asset = "base_mode_superior"; Source = "selector-base-mode-superior.png"; Style = "native" },
    @{ Asset = "base_mode_magic"; Source = "selector-base-mode-magic.png"; Style = "native" },
    @{ Asset = "base_mode_ethereal"; Source = "selector-base-mode-ethereal.png"; Style = "native" },
    @{ Asset = "base_mode_superior_ethereal"; Source = "selector-base-mode-superior-ethereal.png"; Style = "native" },
    @{ Asset = "charm_bonus_random"; Source = "selector-charm-random.png"; Style = "native" },
    @{ Asset = "charm_bonus_vitality"; Source = "selector-charm-vitality.png"; Style = "native" },
    @{ Asset = "charm_bonus_fhr"; Source = "selector-charm-fhr.png"; Style = "native" },
    @{ Asset = "charm_bonus_movement"; Source = "selector-charm-movement.png"; Style = "native" },
    @{ Asset = "charm_bonus_strength"; Source = "selector-charm-strength.png"; Style = "native" },
    @{ Asset = "charm_bonus_dexterity"; Source = "selector-charm-dexterity.png"; Style = "native" },
    @{ Asset = "charm_bonus_magic_find"; Source = "selector-charm-magic-find.png"; Style = "native" },
    @{ Asset = "socket_1"; Source = "selector-socket-1.png"; Style = "native" },
    @{ Asset = "socket_2"; Source = "selector-socket-2.png"; Style = "native" },
    @{ Asset = "socket_3"; Source = "selector-socket-3.png"; Style = "native" },
    @{ Asset = "socket_4"; Source = "selector-socket-4.png"; Style = "native" },
    @{ Asset = "socket_5"; Source = "selector-socket-5.png"; Style = "native" },
    @{ Asset = "socket_6"; Source = "selector-socket-6.png"; Style = "native" },
    @{ Asset = "quick_craft"; Source = "selector-quick-craft.png"; Style = "native" }
)

function Get-Filter([string]$style, [int]$size) {
    $styleFilter = switch ($style) {
        "set" { "hue=h=75:s=1.15" }
        "base" { "hue=s=0,eq=brightness=0.06:contrast=1.05" }
        default { $null }
    }

    $scale = "scale=${size}:${size}:flags=lanczos"
    if ($styleFilter) {
        return "$styleFilter,$scale"
    }

    return $scale
}

function Write-Sprite([string]$sourcePath, [string]$style, [int]$size, [string]$destinationPath) {
    $rawPath = Join-Path $workRoot ([System.IO.Path]::GetRandomFileName() + ".rgba")
    try {
        & $ffmpeg.Source -loglevel error -y -i $sourcePath -vf (Get-Filter $style $size) `
            -frames:v 1 -pix_fmt rgba -f rawvideo $rawPath
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg failed while rendering '$sourcePath' at ${size}px."
        }

        $pixels = [System.IO.File]::ReadAllBytes($rawPath)
        if ($pixels.Length -ne $size * $size * 4) {
            throw "Rendered sprite has an unexpected byte count: $($pixels.Length)"
        }

        $header = New-Object byte[] 40
        [System.Text.Encoding]::ASCII.GetBytes("SpA1").CopyTo($header, 0)
        [System.BitConverter]::GetBytes([uint16]31).CopyTo($header, 4)
        [System.BitConverter]::GetBytes([uint16]$size).CopyTo($header, 6)
        [System.BitConverter]::GetBytes([int]$size).CopyTo($header, 8)
        [System.BitConverter]::GetBytes([int]$size).CopyTo($header, 12)
        [System.BitConverter]::GetBytes([int]1).CopyTo($header, 20)

        $stream = [System.IO.File]::Open(
            $destinationPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $stream.Write($header, 0, $header.Length)
            $stream.Write($pixels, 0, $pixels.Length)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        if (Test-Path -LiteralPath $rawPath) {
            Remove-Item -LiteralPath $rawPath -Force
        }
    }
}

$templatePath = Join-Path $definitionRoot "unique_sword.json"
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "The validated unique-sword UnitDefinition template is missing: $templatePath"
}
$template = [System.IO.File]::ReadAllText($templatePath)
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

foreach ($definition in $definitions) {
    $sourcePath = Join-Path $sourceRoot $definition.Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Selector icon source is missing: $sourcePath"
    }

    $spritePath = Join-Path $spriteRoot ($definition.Asset + ".sprite")
    $lowEndPath = Join-Path $spriteRoot ($definition.Asset + ".lowend.sprite")
    $jsonPath = Join-Path $definitionRoot ($definition.Asset + ".json")
    Write-Sprite $sourcePath $definition.Style 98 $spritePath
    Write-Sprite $sourcePath $definition.Style 49 $lowEndPath
    [System.IO.File]::WriteAllText(
        $jsonPath,
        $template.Replace('"name": "unique_sword"', '"name": "' + $definition.Asset + '"'),
        $utf8WithoutBom)
}

$manifestPath = Join-Path $modDataRoot "package-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "The supply package manifest is missing: $manifestPath"
}

$manifestJson = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifestJson | ConvertFrom-Json
$manifestFiles = @{}
foreach ($file in $manifest.Files) {
    $manifestFiles[$file.RelativePath] = $file
}

foreach ($definition in $definitions) {
    $fileNames = @(
        "$($definition.Asset).sprite"
        "$($definition.Asset).lowend.sprite"
    )
    foreach ($fileName in $fileNames) {
        $relativePath = "data/hd/global/ui/items/misc/jm_selectors/$fileName"
        if (-not $manifestFiles.ContainsKey($relativePath)) {
            throw "The selector sprite is absent from the package manifest: $relativePath"
        }

        $spritePath = Join-Path $spriteRoot $fileName
        $hash = (Get-FileHash -LiteralPath $spritePath -Algorithm SHA256).Hash
        $pattern = '("RelativePath"\s*:\s*"' +
                   [regex]::Escape($relativePath) +
                   '"\s*,\s*"Sha256"\s*:\s*")[A-Fa-f0-9]{64}(")'
        if ([regex]::Matches($manifestJson, $pattern).Count -ne 1) {
            throw "The selector sprite does not have exactly one manifest hash: $relativePath"
        }

        $manifestJson = [regex]::Replace(
            $manifestJson,
            $pattern,
            { param($match) $match.Groups[1].Value + $hash + $match.Groups[2].Value },
            1)
    }
}

[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    $utf8WithoutBom)

Write-Host "Built $($definitions.Count) selector icon assets and refreshed their manifest hashes."
