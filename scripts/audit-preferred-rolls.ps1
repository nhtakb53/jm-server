[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$GeneratedDirectory
)

$ErrorActionPreference = "Stop"
$sourcePath = [System.IO.Path]::GetFullPath($SourceDirectory)
$generatedExcelPath = [System.IO.Path]::GetFullPath(
    (Join-Path $GeneratedDirectory "data\global\excel"))

$properties = Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $sourcePath "properties.txt")
$functionByCode = @{}
foreach ($property in $properties) {
    $functionByCode[$property.code] = [int]$property.func1
}

$tableSpecifications = @(
    @{ File = "uniqueitems.txt"; Slots = 1..12; Property = "prop{0}"; Minimum = "min{0}"; Maximum = "max{0}"; Parameter = "par{0}"; Name = "index"; Percentile = 75 },
    @{ File = "setitems.txt"; Slots = 1..9; Property = "prop{0}"; Minimum = "min{0}"; Maximum = "max{0}"; Parameter = "par{0}"; Name = "index"; Percentile = 75 },
    @{ File = "magicprefix.txt"; Slots = 1..3; Property = "mod{0}code"; Minimum = "mod{0}min"; Maximum = "mod{0}max"; Parameter = "mod{0}param"; Name = "Name"; Percentile = 90 },
    @{ File = "magicsuffix.txt"; Slots = 1..3; Property = "mod{0}code"; Minimum = "mod{0}min"; Maximum = "mod{0}max"; Parameter = "mod{0}param"; Name = "Name"; Percentile = 90 },
    @{ File = "automagic.txt"; Slots = 1..3; Property = "mod{0}code"; Minimum = "mod{0}min"; Maximum = "mod{0}max"; Parameter = "mod{0}param"; Name = "Name"; Percentile = 90 },
    @{ File = "qualityitems.txt"; Slots = 1..2; Property = "mod{0}code"; Minimum = "mod{0}min"; Maximum = "mod{0}max"; Parameter = "mod{0}param"; Name = "mod1code"; Percentile = 90 },
    @{ File = "runes.txt"; Slots = 1..7; Property = "T1Code{0}"; Minimum = "T1Min{0}"; Maximum = "T1Max{0}"; Parameter = "T1Param{0}"; Name = "Name"; Percentile = 75 },
    @{ File = "cubemain.txt"; Slots = 1..5; Property = "mod {0}"; Minimum = "mod {0} min"; Maximum = "mod {0} max"; Parameter = "mod {0} param"; Name = "description"; Percentile = 75 }
)

$expectedChangeCounts = @{
    "automagic.txt" = 32
    "qualityitems.txt" = 12
    "cubemain.txt" = 103
    "magicprefix.txt" = 439
    "magicsuffix.txt" = 310
    "runes.txt" = 140
    "setitems.txt" = 68
    "uniqueitems.txt" = 759
}

$structuralFunctions = @(11, 12, 19, 36)
$excludedPropertyCodes = @(
    "randclassskill", "skill-rand",
    "bloody", "color", "time", "herb", "throw", "state",
    "att-mon%", "dmg-mon%", "dmg-elem-min", "dmg-elem-max", "res-all-max",
    "ease", "levelreq"
)
$knownBeneficialNegativeProperties = @(
    "res-cold", "res-fire", "res-ltng", "res-pois", "red-dmg%", "res-mag"
)

$changes = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$auditedOptionSlots = 0
$auditedParameterFields = 0
$auditedStructuralRanges = 0

foreach ($specification in $tableSpecifications) {
    $sourceRows = @(
        Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $sourcePath $specification.File)
    )
    $generatedRows = @(
        Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $generatedExcelPath $specification.File)
    )
    if ($generatedRows.Count -lt $sourceRows.Count) {
        $errors.Add("$($specification.File): generated row count is smaller than the source.")
        continue
    }

    for ($rowIndex = 0; $rowIndex -lt $sourceRows.Count; $rowIndex++) {
        $sourceRow = $sourceRows[$rowIndex]
        $generatedRow = $generatedRows[$rowIndex]
        foreach ($slot in $specification.Slots) {
            $propertyColumn = $specification.Property -f $slot
            $minimumColumn = $specification.Minimum -f $slot
            $maximumColumn = $specification.Maximum -f $slot
            $parameterColumn = $specification.Parameter -f $slot
            $propertyCode = [string]$sourceRow.$propertyColumn
            $generatedPropertyCode = [string]$generatedRow.$propertyColumn
            $sourceMinimum = [string]$sourceRow.$minimumColumn
            $generatedMinimum = [string]$generatedRow.$minimumColumn
            $sourceMaximum = [string]$sourceRow.$maximumColumn
            $generatedMaximum = [string]$generatedRow.$maximumColumn
            $sourceParameter = [string]$sourceRow.$parameterColumn
            $generatedParameter = [string]$generatedRow.$parameterColumn

            if (-not [string]::IsNullOrWhiteSpace($propertyCode)) {
                $auditedOptionSlots++
                $auditedParameterFields++
                $function = $functionByCode[$propertyCode]
                if ($structuralFunctions -contains $function -or
                    $excludedPropertyCodes -contains $propertyCode) {
                    $auditedStructuralRanges++
                }
            }

            if ($propertyCode -ne $generatedPropertyCode) {
                $errors.Add(
                    "$($specification.File) row $rowIndex '$($sourceRow.($specification.Name))' " +
                    "$propertyColumn changed from '$propertyCode' to '$generatedPropertyCode'.")
            }

            if ($sourceParameter -ne $generatedParameter) {
                $errors.Add(
                    "$($specification.File) row $rowIndex '$($sourceRow.($specification.Name))' " +
                    "$parameterColumn changed from '$sourceParameter' to '$generatedParameter'.")
            }

            $isGheedDiscount =
                $specification.File -eq "uniqueitems.txt" -and
                $sourceRow.($specification.Name) -eq "Gheed's Fortune" -and
                $propertyCode -eq "cheap"
            if ($sourceMaximum -ne $generatedMaximum -and
                -not ($isGheedDiscount -and $generatedMaximum -eq "99")) {
                $errors.Add(
                    "$($specification.File) row $rowIndex '$($sourceRow.($specification.Name))' " +
                    "$maximumColumn unexpectedly changed from '$sourceMaximum' to '$generatedMaximum'.")
            }

            if ($sourceMinimum -eq $generatedMinimum) {
                continue
            }

            $function = $functionByCode[$propertyCode]
            $change = [pscustomobject]@{
                Table = $specification.File
                Name = $sourceRow.($specification.Name)
                Property = $propertyCode
                Function = $function
                OriginalMinimum = $sourceMinimum
                GeneratedMinimum = $generatedMinimum
                Maximum = $sourceMaximum
            }
            $changes.Add($change)

            if ($structuralFunctions -contains $function) {
                $errors.Add(
                    "$($specification.File) row $rowIndex changed structural function $function " +
                    "property '$propertyCode'.")
            }
            if ($excludedPropertyCodes -contains $propertyCode) {
                $errors.Add(
                    "$($specification.File) row $rowIndex changed excluded property '$propertyCode'.")
            }

            if ($isGheedDiscount) {
                if ($generatedMinimum -ne "99") {
                    $errors.Add("Gheed's Fortune discount minimum is not 99.")
                }
                continue
            }

            $minimum = 0
            $maximum = 0
            if (-not [int]::TryParse($sourceMinimum, [ref]$minimum) -or
                -not [int]::TryParse($sourceMaximum, [ref]$maximum)) {
                $errors.Add(
                    "$($specification.File) row $rowIndex changed a non-numeric range for '$propertyCode'.")
                continue
            }

            $spread = [long]$maximum - $minimum
            $offset = [Math]::Floor(
                (($spread * $specification.Percentile) + 99) / 100)
            $expectedMinimum = $minimum + $offset
            if ($generatedMinimum -ne [string]$expectedMinimum) {
                $errors.Add(
                    "$($specification.File) row $rowIndex '$propertyCode' expected minimum " +
                    "$expectedMinimum, got $generatedMinimum.")
            }
            if ($minimum -lt 0 -or $maximum -lt 0) {
                if ($knownBeneficialNegativeProperties -notcontains $propertyCode) {
                    $errors.Add(
                        "$($specification.File) row $rowIndex changed unaudited negative property " +
                        "'$propertyCode' ($minimum..$maximum).")
                }
                if ($minimum -lt 0 -and $maximum -gt 0) {
                    $errors.Add(
                        "$($specification.File) row $rowIndex changed zero-crossing property " +
                        "'$propertyCode' ($minimum..$maximum).")
                }
            }
        }
    }
}

# QualityItems has no frequency field, so legal duplicate rows provide the weighting.
$qualityRows = @(
    Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $generatedExcelPath "qualityitems.txt")
)
if ($qualityRows.Count -ne 29) {
    $errors.Add("qualityitems.txt row count is $($qualityRows.Count); expected 29.")
}
$qualityArmorRows = @($qualityRows | Where-Object { [string]$_.armor -eq "1" })
$qualityWeaponRows = @($qualityRows | Where-Object { [string]$_.weapon -eq "1" })
$qualityArmorDualRows = @($qualityArmorRows | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.mod2code)
})
$qualityWeaponDualRows = @($qualityWeaponRows | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.mod2code)
})
$qualityWeaponDamageRows = @($qualityWeaponRows | Where-Object {
    [string]$_.mod1code -eq "dmg%" -or [string]$_.mod2code -eq "dmg%"
})
if ($qualityArmorRows.Count -ne 10 -or $qualityArmorDualRows.Count -ne 8) {
    $errors.Add(
        "qualityitems.txt armor weighting expected 8 dual rolls out of 10; got " +
        "$($qualityArmorDualRows.Count) out of $($qualityArmorRows.Count).")
}
if ($qualityWeaponRows.Count -ne 20 -or
    $qualityWeaponDualRows.Count -ne 17 -or
    $qualityWeaponDamageRows.Count -ne 16) {
    $errors.Add(
        "qualityitems.txt weapon weighting expected 17 dual and 16 damage rolls out of 20; got " +
        "$($qualityWeaponDualRows.Count) dual and $($qualityWeaponDamageRows.Count) damage out of " +
        "$($qualityWeaponRows.Count).")
}

# Audit every affix weight independently from the preferred-roll minimum audit.
# Fixed charm recipes are the sole extra x8 exception on top of the level multiplier.
$generatedCubeRows = @(
    Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $generatedExcelPath "cubemain.txt")
)
$preferredCharmSuffixIds = [System.Collections.Generic.HashSet[int]]::new()
foreach ($recipe in $generatedCubeRows) {
    $description = [string]$recipe.description
    $output = [string]$recipe.output
    if (($description.StartsWith("JM skill-charms-", [System.StringComparison]::Ordinal) -or
         $description.StartsWith("JM popular-small-charms ", [System.StringComparison]::Ordinal) -or
         $description.StartsWith("JM popular-large-charms ", [System.StringComparison]::Ordinal)) -and
        $output -match '(?:^|,)suf=(\d+)(?:,|"|$)') {
        [void]$preferredCharmSuffixIds.Add([int]$Matches[1])
    }
}
if ($preferredCharmSuffixIds.Count -eq 0) {
    $errors.Add("No fixed charm suffix ids were discovered in generated cube recipes.")
}

foreach ($affixFile in @("magicprefix.txt", "magicsuffix.txt", "automagic.txt")) {
    $sourceRows = @(
        Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $sourcePath $affixFile)
    )
    $generatedRows = @(
        Import-Csv -Delimiter "`t" -LiteralPath (Join-Path $generatedExcelPath $affixFile)
    )
    for ($rowIndex = 0; $rowIndex -lt $sourceRows.Count; $rowIndex++) {
        $sourceFrequency = 0
        $level = 0
        $shouldWeight =
            [string]$sourceRows[$rowIndex].spawnable -eq "1" -and
            [int]::TryParse([string]$sourceRows[$rowIndex].frequency, [ref]$sourceFrequency) -and
            $sourceFrequency -gt 0 -and
            [int]::TryParse([string]$sourceRows[$rowIndex].level, [ref]$level)
        $expectedFrequency = if (-not $shouldWeight) {
            [string]$sourceRows[$rowIndex].frequency
        } else {
            $levelMultiplier = if ($level -lt 20) {
                1
            } elseif ($level -lt 40) {
                3
            } elseif ($level -lt 60) {
                6
            } elseif ($level -lt 80) {
                10
            } else {
                15
            }
            $charmMultiplier = if (
                $affixFile -eq "magicsuffix.txt" -and
                $preferredCharmSuffixIds.Contains($rowIndex)) {
                8
            } else {
                1
            }
            [string]([long]$sourceFrequency * $levelMultiplier * $charmMultiplier)
        }
        $actualFrequency = [string]$generatedRows[$rowIndex].frequency
        if ($actualFrequency -ne $expectedFrequency) {
            $errors.Add(
                "$affixFile row $rowIndex '$($sourceRows[$rowIndex].Name)' frequency expected " +
                "'$expectedFrequency', got '$actualFrequency'.")
        }
    }
}

$actualChangeCounts = @{}
foreach ($group in ($changes | Group-Object Table)) {
    $actualChangeCounts[$group.Name] = $group.Count
}
foreach ($table in $expectedChangeCounts.Keys) {
    $actual = if ($actualChangeCounts.ContainsKey($table)) {
        $actualChangeCounts[$table]
    } else {
        0
    }
    if ($actual -ne $expectedChangeCounts[$table]) {
        $errors.Add(
            "$table changed minimum count is $actual; expected $($expectedChangeCounts[$table]).")
    }
}

if ($errors.Count -ne 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Preferred-roll audit failed with $($errors.Count) error(s)."
}

$negativeChanges = @($changes | Where-Object {
    [int]$_.OriginalMinimum -lt 0 -or [int]$_.Maximum -lt 0
})
Write-Host "Preferred-roll audit passed."
Write-Host "Audited populated option slots: $auditedOptionSlots"
Write-Host "Audited parameter/identity fields: $auditedParameterFields"
Write-Host "Audited structural/excluded ranges: $auditedStructuralRanges"
Write-Host "Changed minimum cells: $($changes.Count)"
foreach ($table in ($actualChangeCounts.Keys | Sort-Object)) {
    Write-Host "  $table`: $($actualChangeCounts[$table])"
}
Write-Host "Structural/compound field changes: 0"
Write-Host "Property-code changes: 0"
Write-Host "Excluded/cosmetic/inverse field changes: 0"
Write-Host "Unexpected maximum changes: 0"
Write-Host "Affix-frequency formula mismatches: 0"
Write-Host "Preferred fixed charm suffix ids: $($preferredCharmSuffixIds.Count)"
Write-Host "Audited negative-range changes: $($negativeChanges.Count)"
