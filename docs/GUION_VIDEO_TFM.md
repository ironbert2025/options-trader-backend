# Guion de narración — Video general TFM

Guion de narración en español para el video general de presentación del sistema, orientado a la evaluación como **Trabajo de Fin de Máster** por parte de un instructor de una escuela de programación. El enfoque es técnico/académico (arquitectura, valor de uso, funcionamiento real), no comercial.

Basado en 6 videos temáticos ya grabados. Se listan aquí en el orden en que se usan dentro de la secuencia general (no en el orden en que fueron grabados) — cada uno valida una unidad funcional independiente del sistema completo:

| # | Unidad validada | Video temático | Link |
|---|---|---|---|
| 1 | Problema (latencia app móvil) | Video 6 | [Ver en Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link) |
| 2 | Arquitectura (EC2, API, BD, Frontend) | Video 1 | [Ver en Google Drive](https://drive.google.com/file/d/18NINnvj5l4r6Ng3eLJrGT7lyHDyf8PP1/view?usp=drive_link) |
| 3 | Configuración inicial | Video 2 | *(pendiente)* |
| 4 | Trade Demo (manual + Target) | Video 3 | [Ver en Google Drive](https://drive.google.com/file/d/1h6SgEcapmUeXq-Pj9rqyZHXs5XLwC_rQ/view?usp=drive_link) |
| 5 | Trade Real (manual + Target) | Video 4 | *(pendiente)* |
| 6 | Frontend / histórico | Video 5 | *(pendiente)* |

📹 **Video general (edición final, todos los clips unidos):** *(pendiente)*

---

## Secuencia sugerida

1. **Problema** (clip móvil recortado, video 6) — plantea el problema medible que origina el proyecto.
2. **Arquitectura del sistema** (video 1) — alcance técnico completo: Clean Architecture, EC2, API, BD, Frontend.
3. **Configuración inicial** (video 2) — evidencia de diseño configurable.
4. **Demo trade** (video 3) — solución al problema planteado, con ambos tipos de salida.
5. **Trade real** (video 4) — prueba de funcionamiento con dinero real.
6. **Frontend / histórico** (video 5) — trazabilidad completa de cada operación.
7. **Cierre** — resumen técnico sin video.

---

## Guion

### Intro (5s, antes del clip móvil)

"Este proyecto nace de un problema real que observé en una academia de trading de opciones en Miami, donde los estudiantes envían sus órdenes al broker desde el móvil."

### 1. Problema — clip móvil recortado (15-20s)

"Este es el flujo actual: login, búsqueda del símbolo, selección del strike, armado de la orden, revisión y envío. Desde que se toma la decisión hasta que la orden llega al mercado pasan no menos de veinte segundos — tiempo suficiente para que el precio se mueva de forma significativa."

📹 [Ver en Google Drive](https://drive.google.com/file/d/1nJOHh_ZM6y3UUC_i5ikqjJbbLqsSNDmv/view?usp=drive_link)

### 2. Arquitectura del sistema — Video EC2/API/BD/Frontend

"Para resolver esto desarrollé una plataforma completa con Clean Architecture en .NET 8: cuatro capas con dependencia unidireccional — Domain, Application, Infrastructure y las capas de presentación, API y WinForms.

El backend está desplegado en una instancia EC2 de AWS: la API ASP.NET Core corre en un puerto, el frontend Angular en otro, ambos servidos de forma independiente. La persistencia usa SQL Server con Entity Framework Core y migraciones versionadas, y las capturas de cada operación se almacenan en S3.

La autenticación es JWT Bearer, con cinco usuarios fijos sembrados en base de datos. La app de escritorio habla directamente con la API de Schwab para datos de mercado y ejecución de órdenes — la API propia solo se usa para login y para persistir trades y screenshots."

📹 [Ver en Google Drive](https://drive.google.com/file/d/18NINnvj5l4r6Ng3eLJrGT7lyHDyf8PP1/view?usp=drive_link)

### 3. Configuración inicial — Video ajuste del programa

"La aplicación es configurable por usuario: credenciales de Schwab, cuentas del broker, lista de tickers con sus rangos y expiración, tamaño de posición y porcentaje de target de salida — todo se persiste localmente y se recupera al reabrir la app."

📹 *(pendiente — link video 2)*

### 4. Demo trade — Video simulado (salida manual + Target automático)

"Aquí ejecuto una operación en modo simulado. Un solo clic sobre la fila del strike en el grid dispara la orden — todo el proceso que en el móvil tomaba veinte segundos aquí se completa en menos de tres.

Puedo cerrar la posición manualmente en cualquier momento, o usar Trade-Target, que coloca automáticamente una orden límite de salida al porcentaje de ganancia predefinido y la ejecuta sin intervención."

📹 [Ver en Google Drive](https://drive.google.com/file/d/1h6SgEcapmUeXq-Pj9rqyZHXs5XLwC_rQ/view?usp=drive_link)

### 5. Trade real — Video real (salida manual + Target automático)

"El mismo flujo, ahora con una operación real contra el broker: la orden se envía, se confirma vía HTTP y el precio de llenado se sincroniza en tiempo real. Cierro una posición manualmente y otra vía Trade-Target, para demostrar que ambos mecanismos de salida funcionan igual con dinero real que en el modo simulado."

📹 *(pendiente — link video 4)*

### 6. Frontend — Video histórico de trades

"Cada trade queda asociado al usuario que lo ejecutó y persistido en base de datos junto con sus capturas de pantalla. El frontend en Angular expone esta información en modo solo lectura: precio de entrada y salida, resultado, fecha, y las imágenes capturadas automáticamente en el momento de la apertura y el cierre de la operación."

📹 *(pendiente — link video 5)*

### Cierre (10-15s, sin video o con logo/diagrama final)

"Stack: .NET 8, ASP.NET Core, SQL Server, Angular, desplegado en AWS EC2 con S3 para almacenamiento. El resultado es una plataforma que resuelve un problema medible de latencia operativa, con trazabilidad completa de cada operación y una arquitectura preparada para escalar a otros brokers. Gracias."

---

## Notas de edición

- Los segmentos 1 y 6 deben ser los más cortos (15-20s) — son contexto/evidencia, no el foco técnico.
- Los segmentos 2 y 5 son los de mayor peso técnico — ahí es donde se juzga la profundidad de arquitectura y el funcionamiento real.
- Evitar palabras como "vender", "cliente", "interesados" — este guion es para evaluación académica, no pitch comercial.
- Si el tiempo total se ajusta, recortar primero el cierre, no el segmento de arquitectura (2) ni el de trade real (5).
