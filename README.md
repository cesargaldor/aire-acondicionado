# Aire

App para la barra de menús de macOS que controla un aire acondicionado **Midea** directamente
por la red local. No necesita la app de Midea, ni servidores, ni Python: el Mac habla con el aire
por tu WiFi.

- Encender y apagar, temperatura (16–30 °C), modo (Auto, Frío, Seco, Calor, Ventilar),
  velocidad del ventilador y Turbo.
- Temperatura de la habitación y de fuera, en el panel y en la barra de menús.
- Pitido del aire al recibir órdenes (se puede desactivar en Ajustes).
- Si el router le cambia la IP al aire, la app lo vuelve a encontrar sola.

El protocolo es un port a Swift de [msmart-ng](https://github.com/mill1000/midea-msmart), y cada
compilación comprueba que produce exactamente los mismos bytes que msmart-ng.

> Proyecto personal, sin relación con Midea. Úsalo bajo tu responsabilidad.

## ¿Funciona con mi aire?

Probablemente, si cumple esto:

- Es un aire **Midea** o de una marca que usa su tecnología (Comfee, Inventor, Pro Breeze…),
  con **WiFi integrado o un módulo WiFi de Midea**.
- Lo controlas con la app **MSmartHome** (la "SmartHome" de Midea).
- El Mac y el aire están en **la misma red WiFi**.

Algunos modelos no tienen todas las funciones (Turbo, ciertas velocidades del ventilador…).
Si no las tienen, simplemente ignoran la orden.

## Antes de empezar

Necesitas:

1. **Un Mac con macOS 13 (Ventura) o posterior.**
2. **Las herramientas de compilación de Apple.** Si ya tienes Xcode, no hace falta nada más.
   Si no, abre el Terminal y ejecuta:
   ```
   xcode-select --install
   ```
   Si te dice que ya están instaladas, perfecto.
3. **El aire configurado en la app MSmartHome** y conectado a tu WiFi. Si nunca lo has hecho,
   instala MSmartHome en el móvil, crea una cuenta y añade el aire siguiendo la app.
4. **El correo y la contraseña de tu cuenta de MSmartHome.** Los aires modernos (protocolo
   "V3", casi todos desde 2020) cifran la comunicación con un *token* y una *key* propios de cada
   aparato. La app los pide **una sola vez** a la nube de Midea con tu cuenta; después todo
   funciona en local. Los aires antiguos ("V2") no necesitan cuenta.

### Sobre tu cuenta y tus datos

- La contraseña **solo se usa en ese primer inicio de sesión y no se guarda** en ningún sitio.
- El token y la key del aire se guardan en el **Llavero de macOS** (busca "Aire" en Acceso a
  Llaveros si quieres verlos o borrarlos).
- A partir de ahí la app no vuelve a conectarse a internet. Si quieres, puedes bloquear el acceso
  a internet del aire desde el router: seguirá funcionando.
- El token y la key no caducan mientras no resetees el aire ni lo vuelvas a emparejar en
  MSmartHome. Si lo haces, en la app pulsa Ajustes → "Olvidar este aire" y vuelve a entrar.

## Instalación

```
git clone <url-de-este-repositorio> aire
cd aire
./build.sh
```

El script hace tres cosas:

1. Compila `build/aire-cli`, una herramienta de terminal para pruebas.
2. Comprueba que el protocolo en Swift coincide con msmart-ng (cifrado, tramas, handshake,
   firmas de la nube). Si algo no coincide, se para aquí.
3. Compila `build/Aire.app`.

Después mueve la app a Aplicaciones y ábrela:

```
mv build/Aire.app /Applications/
open /Applications/Aire.app
```

Aparecerá un icono de aire acondicionado en la barra de menús, arriba a la derecha.

## Primera vez

1. macOS preguntará si permites a Aire **buscar dispositivos en la red local**: pulsa
   **Permitir**. Sin este permiso no puede encontrar el aire. (Si lo denegaste, actívalo en
   Ajustes del Sistema → Privacidad y seguridad → Red local.)
2. Haz clic en el icono: la app busca el aire en tu WiFi.
3. Si es un aire V3, te pedirá el **correo y la contraseña de MSmartHome**. Escríbelos y pulsa
   **Conectar**. Tarda unos segundos.
4. Listo. Para que se abra sola al encender el Mac: icono de ajustes (abajo a la derecha) →
   "Abrir al iniciar sesión".

### Alternativa: sin iniciar sesión en la app

Si ya tienes el token y la key de tu aire (por ejemplo, sacados con `msmart-ng discover`),
guárdalos en un `config.json` así:

```json
{
  "ip": "192.168.1.50",
  "id": 151732605161920,
  "token": "…128 caracteres hexadecimales…",
  "key": "…64 caracteres hexadecimales…"
}
```

y en la pantalla de inicio de sesión pulsa **Importar config.json**.

`aire-cli login` también crea ese archivo (`aire-config.json`) preguntándote la cuenta en el
terminal. **No subas nunca ese archivo a ningún sitio**: con él se controla tu aire.

## aire-cli (opcional)

Sirve para probar paso a paso o para usar el aire desde scripts:

```
build/aire-cli discover                          # ¿encuentra el aire?
build/aire-cli login                             # crea aire-config.json con tu cuenta
build/aire-cli state aire-config.json -v         # lee el estado (con -v muestra el tráfico)
build/aire-cli set aire-config.json power=on temp=24 mode=cool fan=auto
build/aire-cli set aire-config.json turbo=on beep=off
build/aire-cli selftest Tests/vectors.json       # compara con msmart-ng, no toca el aire
```

Opciones de `set`: `power=on|off`, `temp=16…30`, `mode=auto|cool|dry|heat|fan`,
`fan=auto|silent|low|medium|high|max`, `turbo=on|off`, `beep=on|off`.
`aire-cli` también acepta `eco`, `sleep`, `swing` y `display` para los modelos que los tienen.

## Problemas frecuentes

| Problema | Qué hacer |
|---|---|
| "No se encontró ningún aire" | Comprueba que el Mac está en la misma WiFi que el aire (no en una red de invitados) y que diste permiso de red local. Prueba `build/aire-cli discover`. |
| "Correo o contraseña incorrectos" | Usa la cuenta de **MSmartHome**, no la de otras apps de Midea (NetHome Plus, Midea Air…). |
| "La cuenta no tiene este aire" | El aire tiene que estar añadido a esa cuenta en MSmartHome. |
| Funcionaba y deja de responder | Si reseteaste el aire o lo volviste a emparejar, Ajustes → "Olvidar este aire" y vuelve a entrar. |
| `xcode-select --install` da error | Si dice que ya están instaladas, no pasa nada. Si `./build.sh` no encuentra `swiftc`, ejecuta `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer`. |

## Estructura

```
App/AireApp.swift     interfaz (SwiftUI, barra de menús)
MideaKit/             protocolo: descubrimiento, cifrado V2/V3, tramas, nube, Llavero
CLI/main.swift        aire-cli
Tests/                vectores de prueba generados con msmart-ng (gen_vectors.py)
build.sh              compila, comprueba y empaqueta
```

Para regenerar los vectores: `pip install msmart-ng pycryptodome` y
`python Tests/gen_vectors.py > Tests/vectors.json`.

## Créditos y licencia

El protocolo está portado de [msmart-ng](https://github.com/mill1000/midea-msmart)
(licencia MIT, © Tucker Kern, Colin Kuebler, Mac_Zhou, NeoAcheron). Ver [LICENSE](LICENSE).
