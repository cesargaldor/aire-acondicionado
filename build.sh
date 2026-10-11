#!/bin/bash
# Compila y comprueba Aire. Requiere las Command Line Tools:  xcode-select --install
set -e
cd "$(dirname "$0")"

if ! command -v swiftc >/dev/null; then
  echo "Falta el compilador de Swift. Ejecuta:  xcode-select --install"
  exit 1
fi

TARGET="$(uname -m)-apple-macos13.0"
mkdir -p build

echo "1/3  Compilando la herramienta de prueba..."
swiftc -O -target "$TARGET" -module-name AireCLI MideaKit/*.swift CLI/main.swift -o build/aire-cli

echo "2/3  Comprobando el protocolo contra msmart-ng..."
build/aire-cli selftest Tests/vectors.json

echo "3/3  Compilando Aire.app..."
APP="build/Aire.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
swiftc -O -parse-as-library -target "$TARGET" -module-name Aire \
  MideaKit/*.swift App/AireApp.swift -o "$APP/Contents/MacOS/Aire"
cp Info.plist "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"

echo
echo "Listo: $(pwd)/$APP"
echo "Muévela a Aplicaciones y ábrela."
