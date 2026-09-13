# Descargas

**[⬇️ Ir a la última versión](https://github.com/wcalcagno/pdf_signer/releases/latest)**

Los instaladores se publican como archivos adjuntos de cada versión, no dentro del
repositorio. Es lo habitual en proyectos de código abierto: un instalador de decenas de
megabytes por versión quedaría en el historial de Git para siempre y clonar el repositorio
acabaría siendo lento e incómodo para quien solo quiere leer el código.

---

## Windows

| | |
|---|---|
| **Archivo** | `PdfSigner-<versión>-win-x64.msi` |
| **Requisitos** | Windows 10 build 19041 o superior |
| **Tamaño** | ~36 MB |

Descarga el `.msi` y ábrelo. **No necesitas permisos de administrador**: la aplicación se
instala en tu perfil de usuario, no en Archivos de programa.

Tampoco hace falta tener instalado .NET ni el Windows App SDK; van incluidos.

> **Windows mostrará un aviso de SmartScreen** diciendo que el editor es desconocido. Es
> lo esperado: firmar digitalmente un instalador requiere un certificado de pago que este
> proyecto no tiene. Pulsa **Más información → Ejecutar de todas formas**.
>
> Si prefieres no saltarte el aviso, puedes compilar el instalador tú mismo siguiendo las
> instrucciones del [README principal](../README.md#compilar).

Para desinstalar: **Configuración → Aplicaciones → PdfSigner → Desinstalar**.

---

## Android

| | |
|---|---|
| **Archivo** | `com.companyname.pdfsigner.app-Signed.apk` |
| **Requisitos** | Android 5.0 (API 21) o superior |

Descarga el `.apk` en el teléfono y ábrelo. Android pedirá permiso para instalar
aplicaciones de origen desconocido; hay que concedérselo a la aplicación desde la que
abras el archivo (normalmente el navegador o el gestor de archivos).

> El APK está firmado con una clave de depuración, que es lo estándar para distribución
> fuera de Google Play. **No se puede instalar encima de una versión firmada con otra
> clave**: desinstala primero la anterior.

---

## macOS

| | |
|---|---|
| **Archivo** | `PdfSigner-macos.zip` |
| **Requisitos** | macOS 15 o superior |

Descomprime el `.zip` y arrastra `PdfSigner.app` a tu carpeta de Aplicaciones.

> **macOS bloqueará la aplicación la primera vez** porque no está firmada ni notarizada
> (ambas cosas requieren una cuenta de desarrollador de Apple de pago). Para abrirla:
>
> 1. Haz **clic derecho** sobre la aplicación y elige **Abrir**.
> 2. Confirma en el diálogo que aparece.
>
> Solo hace falta la primera vez. Si el sistema se niega igualmente, ejecuta en el
> Terminal:
>
> ```bash
> xattr -dr com.apple.quarantine /Applications/PdfSigner.app
> ```

---

## iOS

**No hay descarga disponible, y no la habrá sin un cambio de circunstancias.**

Apple no permite instalar aplicaciones fuera de la App Store ni de TestFlight, y publicar
en cualquiera de las dos exige una cuenta de desarrollador de pago (99 USD al año) junto
con certificados y perfiles de aprovisionamiento que no pueden vivir en un repositorio
público.

Si quieres usarla en tu iPhone, puedes compilarla e instalarla tú mismo con un Mac, Xcode
y tu Apple ID gratuito. Ten en cuenta que con una cuenta gratuita la aplicación **caduca a
los 7 días** y hay que volver a instalarla.

---

## Verificar lo que descargas

Cada versión incluye las sumas de verificación de sus archivos. Para comprobar que la
descarga no se ha corrompido:

```bash
sha256sum PdfSigner-1.0.0-win-x64.msi
```

En Windows:

```bash
certutil -hashfile PdfSigner-1.0.0-win-x64.msi SHA256
```

---

## Compilarlo tú mismo

Si prefieres no fiarte de un binario descargado —una postura razonable con software sin
firmar— todo el proceso de construcción está en el
[README principal](../README.md#compilar) y en el
[workflow de paquetes](../.github/workflows/release.yml), que es exactamente lo que genera
estos archivos.
