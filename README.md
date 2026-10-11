# Aire

Controla un aire acondicionado **Midea** directamente por la red local, sin la app de Midea ni
servidores. La primera vez se inicia sesión con la cuenta de MSmartHome para obtener la clave del
aire; después todo funciona en local.

| Versión | Dónde está | Cómo se usa |
|---|---|---|
| **macOS** | [`mac/`](mac/README.md) | App en la barra de menús, arriba a la derecha |
| **Windows** | [`windows/`](windows/README.md) | Icono en la bandeja del sistema, junto al reloj |

Las dos versiones implementan el mismo protocolo (un port de
[msmart-ng](https://github.com/mill1000/midea-msmart)) y cada compilación lo compara byte a byte
con msmart-ng usando los vectores de [`mac/Tests/vectors.json`](mac/Tests/vectors.json).

> Proyecto personal, sin relación con Midea. Úsalo bajo tu responsabilidad.

Licencia: ver [LICENSE](LICENSE).
