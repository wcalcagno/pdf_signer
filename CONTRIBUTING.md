# Cómo contribuir

Gracias por el interés. Este proyecto nació de una necesidad concreta —firmar PDFs sin
pagar licencias caras por algo tan simple— y toda ayuda para mejorarlo es bienvenida.

## Formas de aportar

No hace falta escribir código para ayudar:

- **Reportar errores.** Si una firma sale desplazada o girada, abre un issue e incluye,
  si puedes, el PDF que lo provoca (o uno equivalente sin datos sensibles). El tamaño de
  página y la rotación del documento son casi siempre la clave.
- **Probar en plataformas.** No todas las plataformas están igual de verificadas; el
  README dice cuál es el estado real de cada una. Un reporte de "funciona en X" o
  "falla en Y" tiene mucho valor.
- **Traducir** la interfaz o la documentación.
- **Proponer mejoras** en un issue antes de invertir horas en un pull request grande.

## Preparar el entorno

Necesitas el SDK de .NET 10. Para trabajar solo en la lógica de firma no hace falta
instalar los workloads de MAUI:

```bash
git clone https://github.com/wcalcagno/pdf_signer.git
cd pdf_signer
dotnet test tests/PdfSigner.Core.Tests/PdfSigner.Core.Tests.csproj
```

Para compilar la aplicación sí necesitas los workloads:

```bash
dotnet workload install maui
```

## Pautas de código

- **La lógica de PDF va en `PdfSigner.Core`, sin referencias a MAUI.** Es lo que permite
  que los tests corran en CI sin emuladores ni un Mac. Si una funcionalidad necesita
  MAUI, plantéala como una interfaz en el core y su implementación en la app.
- **Todo cambio en coordenadas o geometría necesita un test.** Es la parte del código
  donde los errores son silenciosos: el PDF se genera sin fallar y el problema solo se ve
  al abrir el archivo. Fíjate en `PageGeometryTests` para el estilo.
- **Comentarios en español y que expliquen el porqué**, no el qué. Si el comentario
  repite lo que dice la línea siguiente, sobra.
- Nombres de tests descriptivos en español, como el resto de la suite.

## Pull requests

1. Crea una rama desde `main`.
2. Asegúrate de que `dotnet test` pasa en verde.
3. Describe qué problema resuelve el cambio. Si arregla un error de posicionamiento,
   menciona el tipo de PDF (rotado, apaisado, MediaBox desplazado) que lo reproducía.

## Fuera de alcance

Este proyecto hace firma **visual**, no criptográfica. Las propuestas de PAdES,
certificados X.509 o validación de firmas de terceros son bienvenidas como discusión,
pero son un cambio de alcance mayor y están en el roadmap, no en esta versión.
