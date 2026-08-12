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
    @{ Asset = "unique_axe"; Source = "weapon-axe.png"; Style = "unique" },
    @{ Asset = "unique_blunt"; Source = "weapon-blunt.png"; Style = "unique" },
    @{ Asset = "unique_bow"; Source = "weapon-bow.png"; Style = "unique" },
    @{ Asset = "unique_pole"; Source = "weapon-pole.png"; Style = "unique" },
    @{ Asset = "unique_dagger"; Source = "weapon-dagger.png"; Style = "unique" },
    @{ Asset = "unique_caster"; Source = "weapon-caster.png"; Style = "unique" },
    @{ Asset = "unique_class_weapon"; Source = "weapon-class.png"; Style = "unique" },
    @{ Asset = "unique_body"; Source = "armor-body.png"; Style = "unique" },
    @{ Asset = "unique_helm"; Source = "armor-helm.png"; Style = "unique" },
    @{ Asset = "unique_shield"; Source = "armor-shield.png"; Style = "unique" },
    @{ Asset = "unique_gloves"; Source = "armor-gloves.png"; Style = "unique" },
    @{ Asset = "unique_boots"; Source = "armor-boots.png"; Style = "unique" },
    @{ Asset = "unique_belts"; Source = "armor-belt.png"; Style = "unique" },
    @{ Asset = "unique_class_armor"; Source = "armor-class.png"; Style = "unique" },
    @{ Asset = "unique_rings"; Source = "jewelry-ring.png"; Style = "unique" },
    @{ Asset = "unique_amulets"; Source = "jewelry-amulet.png"; Style = "unique" },
    @{ Asset = "unique_charms"; Source = "charms-jewel.png"; Style = "unique" },
    @{ Asset = "unique_other"; Source = "charms-jewel.png"; Style = "unique" },

    # Set selectors are green-shifted so their quality is visible before reading the name.
    @{ Asset = "set_weapons"; Source = "selector-set-weapons.png"; Style = "native" },
    @{ Asset = "set_body"; Source = "armor-body.png"; Style = "set" },
    @{ Asset = "set_helm"; Source = "armor-helm.png"; Style = "set" },
    @{ Asset = "set_shield"; Source = "armor-shield.png"; Style = "set" },
    @{ Asset = "set_gloves"; Source = "armor-gloves.png"; Style = "set" },
    @{ Asset = "set_boots"; Source = "armor-boots.png"; Style = "set" },
    @{ Asset = "set_belts"; Source = "armor-belt.png"; Style = "set" },
    @{ Asset = "set_class_armor"; Source = "armor-class.png"; Style = "set" },
    @{ Asset = "set_jewelry"; Source = "jewelry-ring.png"; Style = "set" },
    @{ Asset = "set_other"; Source = "charms-jewel.png"; Style = "set" },

    # Base selectors use a silver treatment, distinct from unique and set selectors.
    @{ Asset = "base_sword"; Source = "selector-base-sword.png"; Style = "native" },
    @{ Asset = "base_axe"; Source = "weapon-axe.png"; Style = "base" },
    @{ Asset = "base_blunt"; Source = "weapon-blunt.png"; Style = "base" },
    @{ Asset = "base_bow"; Source = "weapon-bow.png"; Style = "base" },
    @{ Asset = "base_pole"; Source = "weapon-pole.png"; Style = "base" },
    @{ Asset = "base_dagger"; Source = "weapon-dagger.png"; Style = "base" },
    @{ Asset = "base_caster"; Source = "weapon-caster.png"; Style = "base" },
    @{ Asset = "base_class_weapon"; Source = "weapon-class.png"; Style = "base" },
    @{ Asset = "base_body"; Source = "armor-body.png"; Style = "base" },
    @{ Asset = "base_helm"; Source = "armor-helm.png"; Style = "base" },
    @{ Asset = "base_shield"; Source = "armor-shield.png"; Style = "base" },
    @{ Asset = "base_gloves"; Source = "armor-gloves.png"; Style = "base" },
    @{ Asset = "base_boots"; Source = "armor-boots.png"; Style = "base" },
    @{ Asset = "base_belts"; Source = "armor-belt.png"; Style = "base" },
    @{ Asset = "base_class_armor"; Source = "armor-class.png"; Style = "base" },

    @{ Asset = "materials_runes"; Source = "material-rune.png"; Style = "unique" },
    @{ Asset = "materials_gems"; Source = "material-gem.png"; Style = "unique" },
    @{ Asset = "materials_charms"; Source = "charms-jewel.png"; Style = "unique" },

    @{ Asset = "skill_charms_amazon"; Source = "skill-amazon.png"; Style = "unique" },
    @{ Asset = "skill_charms_sorceress"; Source = "skill-sorceress.png"; Style = "unique" },
    @{ Asset = "skill_charms_necromancer"; Source = "skill-necromancer.png"; Style = "unique" },
    @{ Asset = "skill_charms_paladin"; Source = "skill-paladin.png"; Style = "unique" },
    @{ Asset = "skill_charms_barbarian"; Source = "skill-barbarian.png"; Style = "unique" },
    @{ Asset = "skill_charms_druid"; Source = "skill-druid.png"; Style = "unique" },
    @{ Asset = "skill_charms_assassin"; Source = "skill-assassin.png"; Style = "unique" },
    @{ Asset = "skill_charms_warlock"; Source = "skill-warlock.png"; Style = "unique" },
    @{ Asset = "popular_small_charms"; Source = "popular-charms.png"; Style = "unique" },
    @{ Asset = "popular_large_charms"; Source = "popular-charms.png"; Style = "unique" },

    @{ Asset = "base_mode_normal"; Source = "mode-normal.png"; Style = "unique" },
    @{ Asset = "base_mode_superior"; Source = "mode-superior.png"; Style = "unique" },
    @{ Asset = "base_mode_magic"; Source = "mode-magic.png"; Style = "unique" },
    @{ Asset = "base_mode_ethereal"; Source = "mode-ethereal.png"; Style = "unique" },
    @{ Asset = "base_mode_superior_ethereal"; Source = "mode-superior-ethereal.png"; Style = "unique" },
    @{ Asset = "charm_bonus_random"; Source = "option-random.png"; Style = "unique" },
    @{ Asset = "charm_bonus_vitality"; Source = "selector-charm-vitality.png"; Style = "native" },
    @{ Asset = "charm_bonus_fhr"; Source = "option-fhr.png"; Style = "unique" },
    @{ Asset = "charm_bonus_movement"; Source = "option-movement.png"; Style = "unique" },
    @{ Asset = "charm_bonus_strength"; Source = "option-strength.png"; Style = "unique" },
    @{ Asset = "charm_bonus_dexterity"; Source = "option-dexterity.png"; Style = "unique" },
    @{ Asset = "charm_bonus_magic_find"; Source = "option-magic-find.png"; Style = "unique" },
    @{ Asset = "socket_1"; Source = "socket-1.png"; Style = "unique" },
    @{ Asset = "socket_2"; Source = "socket-2.png"; Style = "unique" },
    @{ Asset = "socket_3"; Source = "socket-3.png"; Style = "unique" },
    @{ Asset = "socket_4"; Source = "socket-4.png"; Style = "unique" },
    @{ Asset = "socket_5"; Source = "socket-5.png"; Style = "unique" },
    @{ Asset = "socket_6"; Source = "socket-6.png"; Style = "unique" },
    @{ Asset = "quick_craft"; Source = "quick-craft.png"; Style = "unique" }
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
