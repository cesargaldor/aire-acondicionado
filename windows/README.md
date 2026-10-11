# Aire para Windows

La versión de Windows de Aire: un icono en la bandeja del sistema, junto al reloj, que controla un
aire acondicionado **Midea** directamente por la red local. Al hacer clic se abre el panel encima
del reloj, abajo a la derecha. No necesita la app de Midea, ni servidores, ni Python.

Hace lo mismo que la versión de Mac:

- Encender y apagar, temperatura (16–30 °C), modo, velocidad del ventilador y Turbo.
- Temperatura de la sala y de fuera.
- El icono de la bandeja muestra la temperatura objetivo cuando está encendido, con una raya del
  color del modo (oliva para frío y demás, naranja para calor). Al pasar el ratón, el estado completo.
- Pitido del aire al recibir órdenes (se puede desactivar en Ajustes).
- Si el router le cambia la IP al aire, la app lo vuelve a encontrar sola.

El protocolo (`MideaKit/`) es un port a C# del de la versión de Mac. Cada compilación lo compara
byte a byte con msmart-ng usando los mismos vectores de prueba (`../mac/Tests/vectors.json`).

## Compilar

Necesitas **Windows 10 u 11** y el **SDK de .NET 8** (solo para compilar). Para instalarlo,
abre PowerShell o Terminal y ejecuta:

```
winget install Microsoft.DotNet.SDK.8
```

Después, haz doble clic en **`build.bat`** (o ejecútalo desde una terminal nueva). El script:

1. Compila `aire-cli`, una herramienta de terminal para pruebas.
2. Comprueba que el protocolo coincide con msmart-ng. Si algo no coincide, se para aquí.
3. Crea `build\Aire\Aire.exe`.

`Aire.exe` es **un solo archivo que lleva todo dentro**: puedes copiarlo a cualquier carpeta o a
otro PC (el de un vecino, por ejemplo) y abrirlo sin instalar .NET. Pesa unos 70 MB por eso.

### Al abrirlo por primera vez

- **SmartScreen** puede avisar de "Windows protegió su PC" porque el programa no está firmado:
  pulsa **Más información → Ejecutar de todas formas**.
- Si el **Firewall de Windows** pregunta, permite a Aire en **redes privadas**. Sin eso no puede
  encontrar el aire.
- Windows 11 esconde los iconos nuevos en la flecha **^** de la barra de tareas. Para ver siempre
  la temperatura, arrastra el icono de Aire junto al reloj, o actívalo en
  Configuración → Personalización → Barra de tareas → Otros iconos de la bandeja → Aire.

## Primera vez

1. El panel se abre solo y busca el aire en tu red.
2. Si es un aire V3 (casi todos desde 2020), te pide el **correo y la contraseña de MSmartHome**.
   La contraseña solo se usa para obtener la clave del aire y no se guarda.
3. Listo. Para que se abra al encender el PC: icono de ajustes (abajo a la derecha del panel) →
   "Iniciar con Windows".

Si ya tienes un `config.json` con `ip`, `id`, `token` y `key` (por ejemplo, el que creaste en
Windows con `setup.py`), pulsa **Importar config.json** y te saltas el login.

### Dónde guarda sus datos

- La configuración del aire (con el token y la key) está en `%APPDATA%\Aire\device.dat`,
  **cifrada con DPAPI**: solo tu usuario de Windows en este PC puede leerla. Es el equivalente al
  Llavero del Mac. Para empezar de cero: Ajustes → "Olvidar este aire".
- Los ajustes están en `%APPDATA%\Aire\settings.json`.
- "Iniciar con Windows" añade Aire a la clave `Run` del registro de tu usuario.

## Uso

- **Clic izquierdo** en el icono: abre o cierra el panel. **Esc** o hacer clic fuera lo cierra.
- **Clic derecho**: menú rápido para encender o apagar y para salir.
- En la barra de temperatura también funciona la **rueda del ratón**.

## aire-cli (opcional)

Está en `build\cli\aire-cli.exe` y acepta los mismos comandos que la versión de Mac:

```
build\cli\aire-cli discover                         # ¿encuentra el aire?
build\cli\aire-cli login                            # crea aire-config.json con tu cuenta
build\cli\aire-cli state aire-config.json -v        # lee el estado (con -v muestra el tráfico)
build\cli\aire-cli set aire-config.json power=on temp=24 mode=cool fan=auto turbo=off
build\cli\aire-cli display aire-config.json         # enciende o apaga la pantalla del aire
build\cli\aire-cli selftest ..\mac\Tests\vectors.json   # compara con msmart-ng, no toca el aire
```

## Problemas frecuentes

| Problema | Qué hacer |
|---|---|
| "No se encontró ningún aire" | El PC tiene que estar en la misma red que el aire (no en la de invitados). Revisa que el Firewall deja pasar a Aire en redes privadas y que tu red está marcada como **Privada** en Windows. Prueba `aire-cli discover`. |
| "Correo o contraseña incorrectos" | Usa la cuenta de **MSmartHome**. Si entras con Google o Apple, crea una contraseña desde la app. |
| No veo el icono | Está en la flecha **^** de la barra de tareas (ver arriba). |
| Funcionaba y deja de responder | Si reseteaste o volviste a emparejar el aire, Ajustes → "Olvidar este aire" y vuelve a entrar. |
| `build.bat` dice que falta .NET | Instala el SDK con `winget install Microsoft.DotNet.SDK.8` y abre una terminal **nueva**. |

## Estructura

```
Aire/        la app de la bandeja (WPF): icono, panel, guardado cifrado, inicio con Windows
MideaKit/    protocolo: descubrimiento, cifrado V2/V3, tramas, nube (port de ../mac/MideaKit)
AireCli/     aire-cli
build.bat    compila, comprueba y empaqueta
```
