#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Reorganiza los archivos de documentación (.md) del repositorio a una estructura
    profesional bajo docs/, preserva el historial de Git y actualiza todos los
    enlaces relativos entre documentos.
.DESCRIPTION
    FASE 1 – Crea subcarpetas en docs/
    FASE 2 – Mueve archivos con "git mv" (preserva historial de Git)
    FASE 3 – Actualiza enlaces relativos en todos los .md afectados
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot   # directorio donde vive el script (raíz del repositorio)

# ─────────────────────────────────────────────────────────────────────────────
# FASE 1 – CREAR ESTRUCTURA DE CARPETAS
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n[FASE 1] Creando subcarpetas en docs/..." -ForegroundColor Cyan

$Dirs = @(
    "docs/architecture",
    "docs/guides",
    "docs/releases"
)

foreach ($dir in $Dirs) {
    $fullPath = Join-Path $Root $dir
    if (-not (Test-Path $fullPath)) {
        New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
        Write-Host "  + Creada: $dir"
    } else {
        Write-Host "  ~ Existe: $dir"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# FASE 2 – MOVER ARCHIVOS CON git mv (preserva historial)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n[FASE 2] Moviendo archivos con git mv..." -ForegroundColor Cyan

# Formato: @{ Src = "origen relativa a raíz"; Dst = "destino relativo a raíz" }
$Moves = @(
    # ── DESDE RAÍZ → docs/architecture/
    @{ Src = "ANALISIS_ARQUITECTURA_COMPARATIVA.md";    Dst = "docs/architecture/ANALISIS_ARQUITECTURA_COMPARATIVA.md" },
    @{ Src = "ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md"; Dst = "docs/architecture/ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md" },
    @{ Src = "PROPUESTA_REORGANIZACION_XRMTOOLBOX.md";  Dst = "docs/architecture/PROPUESTA_REORGANIZACION_XRMTOOLBOX.md" },

    # ── DESDE RAÍZ → docs/guides/
    @{ Src = "CONFIGURACION_DATAVERSE.md";              Dst = "docs/guides/CONFIGURACION_DATAVERSE.md" },
    @{ Src = "GUIA_IMPLEMENTACION_HYBRID.md";           Dst = "docs/guides/GUIA_IMPLEMENTACION_HYBRID.md" },
    @{ Src = "GUIA_IMPLEMENTACION_REORGANIZACION.md";   Dst = "docs/guides/GUIA_IMPLEMENTACION_REORGANIZACION.md" },
    @{ Src = "GUIA_INTEGRACION_ENTERPRISE.md";          Dst = "docs/guides/GUIA_INTEGRACION_ENTERPRISE.md" },
    @{ Src = "GUIA-RAPIDA-UI.md";                       Dst = "docs/guides/GUIA-RAPIDA-UI.md" },

    # ── DESDE RAÍZ → docs/  (documentos generales)
    @{ Src = "PROJECT_SUMMARY.md";                      Dst = "docs/PROJECT_SUMMARY.md" },
    @{ Src = "QUICKSTART.md";                           Dst = "docs/QUICKSTART.md" },
    @{ Src = "RESUMEN_EJECUTIVO.md";                    Dst = "docs/RESUMEN_EJECUTIVO.md" },

    # ── DESDE docs/ → docs/architecture/
    @{ Src = "docs/architecture.md";                    Dst = "docs/architecture/architecture.md" },
    @{ Src = "docs/diagrams.md";                        Dst = "docs/architecture/diagrams.md" },

    # ── DESDE docs/ → docs/releases/
    @{ Src = "docs/PR_SUMMARY_UI_SURGICAL_CLEANUP.md";  Dst = "docs/releases/PR_SUMMARY_UI_SURGICAL_CLEANUP.md" },
    @{ Src = "docs/RELEASE_NOTES_v1.0.0-qa-fix.md";     Dst = "docs/releases/RELEASE_NOTES_v1.0.0-qa-fix.md" },
    @{ Src = "docs/UI_BASELINE_REPORT_2026-02-18.md";   Dst = "docs/releases/UI_BASELINE_REPORT_2026-02-18.md" },
    @{ Src = "docs/UI_SURGICAL_CLEANUP_CHECKLIST.md";   Dst = "docs/releases/UI_SURGICAL_CLEANUP_CHECKLIST.md" },

    # ── DESDE docs/ → docs/guides/
    @{ Src = "docs/user-guide.md";                      Dst = "docs/guides/user-guide.md" }
)

Push-Location $Root
try {
    foreach ($m in $Moves) {
        $srcFull = Join-Path $Root $m.Src
        if (Test-Path $srcFull) {
            git mv $m.Src $m.Dst
            Write-Host "  git mv  $($m.Src)  →  $($m.Dst)"
        } else {
            Write-Warning "  OMITIDO (no encontrado): $($m.Src)"
        }
    }
} finally {
    Pop-Location
}

# ─────────────────────────────────────────────────────────────────────────────
# FASE 3 – ACTUALIZAR ENLACES RELATIVOS
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n[FASE 3] Actualizando enlaces relativos en archivos .md..." -ForegroundColor Cyan

<#
  Tabla de sustituciones: cada entrada indica en QUÉ archivo buscar y qué
  reemplazar. Esto es explícito y seguro (no regex global que rompa cosas).

  Archivo destino (ya en nueva ubicación) → [ OldLink, NewLink ]
#>
$LinkFixes = @(
    # README.md (sigue en raíz) apuntaba a GUIA-RAPIDA-UI.md en raíz
    @{
        File    = "README.md"
        Old     = "GUIA-RAPIDA-UI.md"
        New     = "docs/guides/GUIA-RAPIDA-UI.md"
    },

    # docs/PROJECT_SUMMARY.md apuntaba a QUICKSTART.md (raíz → ahora docs/)
    @{
        File    = "docs/PROJECT_SUMMARY.md"
        Old     = "QUICKSTART.md"
        New     = "QUICKSTART.md"   # mismo directorio docs/, sin cambio de ruta
    },

    # docs/RESUMEN_EJECUTIVO.md → ARQUITECTURA (ahora en docs/architecture/)
    @{
        File    = "docs/RESUMEN_EJECUTIVO.md"
        Old     = "ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md"
        New     = "architecture/ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md"
    },
    # docs/RESUMEN_EJECUTIVO.md → GUIA_INTEGRACION_ENTERPRISE (ahora en docs/guides/)
    @{
        File    = "docs/RESUMEN_EJECUTIVO.md"
        Old     = "GUIA_INTEGRACION_ENTERPRISE.md"
        New     = "guides/GUIA_INTEGRACION_ENTERPRISE.md"
    },

    # docs/guides/GUIA_INTEGRACION_ENTERPRISE.md → ARQUITECTURA (ahora en ../architecture/)
    @{
        File    = "docs/guides/GUIA_INTEGRACION_ENTERPRISE.md"
        Old     = "ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md"
        New     = "../architecture/ARQUITECTURA_OPTIMIZACION_EMPRESARIAL.md"
    }
    # Nota: docs/architecture/architecture.md → "./diagrams.md" ya es relativo al mismo
    # directorio (docs/architecture/), no requiere cambio tras el movimiento.
)

foreach ($fix in $LinkFixes) {
    $filePath = Join-Path $Root $fix.File
    if (-not (Test-Path $filePath)) {
        Write-Warning "  OMITIDO (no encontrado): $($fix.File)"
        continue
    }

    $content = Get-Content $filePath -Raw -Encoding utf8
    # Reemplazar tanto "(OldLink)" como "(./OldLink)" para cubrir ambas variantes
    $patterns = @(
        [regex]::Escape("($($fix.Old))"),
        [regex]::Escape("(./$($fix.Old))")
    )
    $newContent = $content
    foreach ($pattern in $patterns) {
        $newContent = $newContent -replace $pattern, "($($fix.New))"
    }

    if ($newContent -ne $content) {
        Set-Content -Path $filePath -Value $newContent -Encoding utf8 -NoNewline
        Write-Host "  Actualizado: $($fix.File)  [$($fix.Old) → $($fix.New)]"
    } else {
        Write-Host "  Sin cambios: $($fix.File)  (enlace '$($fix.Old)' no encontrado o ya correcto)"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# RESUMEN FINAL
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n[OK] Reorganización completada." -ForegroundColor Green
Write-Host "Árbol resultante de docs/:" -ForegroundColor Yellow
Push-Location $Root
try {
    Get-ChildItem -Path "docs" -Recurse -Filter "*.md" |
        ForEach-Object { "  " + $_.FullName.Replace($Root + "\", "") }
} finally {
    Pop-Location
}

Write-Host "`nPróximo paso sugerido:" -ForegroundColor Yellow
Write-Host "  git add -A && git commit -m 'docs: reorganize documentation into docs/ subfolders'"
