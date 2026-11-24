# Qinstall

Instalador rápido, ligero y portable de aplicaciones para Windows (alternativa propia a **Ninite**), diseñado para llevarse directamente en un pendrive USB.

## Características

- **100% Portable y sin dependencias**: Genera un único `Qinstall.exe` de menos de 100 KB que usa el `.NET Framework 4.8` incluido de fábrica en Windows 10 y 11.
- **Modo Online con Winget**: Descarga e instala siempre la última versión oficial de cada programa de forma silenciosa y sin bloatware. Si el equipo no tiene Winget, ofrece instalarlo con un clic.
- **Soporte Híbrido (URL directa y Pendrive Local)**: Además de Winget, puedes agregar instaladores desde cualquier URL directa (`.exe`/`.msi`) o archivos locales guardados en tu pendrive usando rutas relativas (por ejemplo `instaladores\mi_programa.exe`).
- **Catálogo editable sin recompilar (`apps.json`)**: Agrega o quita aplicaciones desde el botón **+ Agregar app** (incluye buscador integrado de Winget) o editando `apps.json`.
- **Interfaz limpia tipo Ninite**: Agrupada por categorías, buscador instantáneo, selección rápida de esenciales, barra de progreso, registro en tiempo real y resumen final.

## Uso en tu pendrive

1. Compila el proyecto ejecutando `build.bat` (o usa el `Qinstall.exe` ya generado).
2. Copia `Qinstall.exe` y `apps.json` a tu pendrive (por ejemplo, en una carpeta `Qinstall\`).
3. Opcionalmente crea una subcarpeta `instaladores\` para tus `.exe`/`.msi` locales.
4. En cualquier PC con Windows 10/11, ejecuta `Qinstall.exe`, marca las aplicaciones y presiona **Instalar seleccionadas**.

## Cómo compilar desde el código fuente

No necesitas instalar Visual Studio ni SDKs adicionales. Solo ejecuta:

```bat
build.bat
```

Esto utiliza el compilador `csc.exe` integrado en Windows para generar `Qinstall.exe`.

## Estructura de `apps.json`

```json
{
  "Id": "7zip",
  "Name": "7-Zip",
  "Category": "Utilidades",
  "Description": "Compresor de archivos ligero y libre",
  "Source": "winget",
  "Target": "7zip.7zip",
  "SilentArgs": "",
  "Essential": true
}
```

Los valores válidos para `Source` son:
- `"winget"`: `Target` es el ID del paquete en Winget (ej. `Google.Chrome`).
- `"url"`: `Target` es el enlace directo al `.exe` o `.msi` y `SilentArgs` los parámetros silenciosos (ej. `/S` o `/VERYSILENT`).
- `"local"`: `Target` es la ruta relativa junto a `Qinstall.exe` (ej. `instaladores\app.exe`).
