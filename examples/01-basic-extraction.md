# Ejemplo 1: Extracción Básica de Auditoría

## Objetivo
Extraer el historial de auditoría de la entidad "account" del último mes y exportarlo a Excel.

## Comando

```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --format excel \
  --output ./exports/monthly/
```

## Parámetros Explicados

- `--entity account`: Especifica que queremos extraer auditoría de la entidad "account" (cuentas)
- `--from 2024-01-01`: Fecha de inicio para la extracción
- `--to 2024-01-31`: Fecha final para la extracción
- `--format excel`: Formato de salida (Excel con múltiples hojas)
- `--output ./exports/monthly/`: Directorio donde se guardará el archivo

## Resultado Esperado

El comando generará un archivo Excel con el siguiente formato:
```
./exports/monthly/audit_extract_20240201_153045.xlsx
```

### Contenido del Archivo Excel

**Hoja 1: Summary**
- Total de registros extraídos
- Fecha de exportación
- Rango de fechas procesado
- Estadísticas por tipo de operación

**Hoja 2: Audit Records**
| Audit ID | Created On | Entity | Record ID | Operation | User Name | Changes Count |
|----------|------------|--------|-----------|-----------|-----------|---------------|
| ... | ... | account | ... | Update | John Doe | 3 |

**Hoja 3: Field Changes**
| Audit ID | Created On | Field Name | Old Value | New Value | Change Description |
|----------|------------|------------|-----------|-----------|-------------------|
| ... | ... | name | Old Corp | New Corp | Changed from 'Old Corp' to 'New Corp' |

## Análisis del Resultado

Una vez generado el archivo Excel:

1. **Abrir en Excel:** El archivo se puede abrir directamente en Microsoft Excel
2. **Filtrar datos:** Usar filtros automáticos en las columnas para buscar información específica
3. **Analizar cambios:** Revisar la hoja "Field Changes" para ver cambios campo por campo
4. **Generar reportes:** Crear gráficas y tablas dinámicas según necesidad

## Variantes del Ejemplo

### Extracción de múltiples meses
```bash
for month in {01..06}; do
  dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
    --entity account \
    --from 2024-${month}-01 \
    --to 2024-${month}-31 \
    --format excel \
    --output ./exports/2024/month-${month}/
done
```

### Extracción solo de creaciones
```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --operation create \
  --format excel
```

### Extracción con exportación a CSV (más ligero)
```bash
dotnet run --project src/AuditHistoryExtractorPro.CLI -- extract \
  --entity account \
  --from 2024-01-01 \
  --to 2024-01-31 \
  --format csv \
  --output ./exports/csv/
```

## Casos de Uso

Este ejemplo es útil para:
- **Compliance mensual:** Generar reportes de auditoría para cumplimiento normativo
- **Revisiones de seguridad:** Analizar cambios realizados en el sistema
- **Auditorías internas:** Proporcionar trazabilidad de modificaciones
- **Reportes ejecutivos:** Presentar actividad del sistema a gerencia

## Solución de Problemas

### Error: "No records found"
- Verificar que existan registros de auditoría en el rango de fechas especificado
- Confirmar que la auditoría esté habilitada para la entidad "account"

### Error: "Authentication failed"
- Revisar las credenciales en `config.yaml`
- Ejecutar `dotnet run --project src/AuditHistoryExtractorPro.CLI -- validate`

### Archivo muy grande
- Reducir el rango de fechas
- Usar formato CSV en lugar de Excel
- Dividir la extracción por semanas en lugar de meses

## Próximos Pasos

- Ver [Ejemplo 2: Filtros Avanzados](./02-advanced-filters.md) para aprender a filtrar por usuario y operación
- Ver [Ejemplo 3: Extracción Incremental](./03-incremental-extraction.md) para extracciones automáticas
