"""
Genera vectores de prueba con msmart-ng (la implementación de referencia en Python).
La herramienta `aire-cli selftest` comprueba que la versión Swift produce los mismos bytes.

    pip install msmart-ng
    python gen_vectors.py > vectors.json
"""
import json
from hashlib import sha256

from Crypto.Cipher import AES

import msmart.lan as lan
from msmart import crc8
from msmart.cloud import SmartHomeCloud
from msmart.device.AC.command import (Command, GetStateCommand, Response,
                                      SetStateCommand, StateResponse,
                                      ToggleDisplayCommand)
from msmart.discover import Discover
from msmart.frame import Frame
from msmart.lan import Security, _LanProtocolV3, _Packet

h = bytes.hex
V = {}

# --- CRC8 y checksum -------------------------------------------------------
sample = bytes(range(0, 250, 7))
V["crc8"] = {"input": h(sample), "crc": crc8.calculate(sample),
             "checksum": Frame.checksum(sample)}

# --- Comandos ---------------------------------------------------------------
Command._message_id = 0
V["get_state"] = {"message_id": 1, "frame": h(GetStateCommand().tobytes())}

cmd = SetStateCommand()
cmd.beep_on = False
cmd.power_on = True
cmd.target_temperature = 23.5
cmd.operational_mode = 2
cmd.fan_speed = 40
cmd.swing_mode = 0xC
cmd.eco = False
cmd.turbo = False
cmd.sleep = False
cmd.fahrenheit = False
cmd.follow_me = False
cmd.purifier = False
cmd.target_humidity = 40
cmd.freeze_protection = False
cmd.aux_heat = False
cmd.independent_aux_heat = False
Command._message_id = 1
V["set_state"] = {"message_id": 2, "power": True, "target": 23.5, "mode": 2, "fan": 40,
                  "swing": 0xC, "humidity": 40, "frame": h(cmd.tobytes())}

cmd.target_temperature = 16.0
cmd.power_on = False
Command._message_id = 2
V["set_state_low"] = {"message_id": 3, "power": False, "target": 16.0, "mode": 2, "fan": 40,
                      "swing": 0xC, "humidity": 40, "frame": h(cmd.tobytes())}

cmd.power_on = True
cmd.target_temperature = 26.0
cmd.operational_mode = 4
cmd.fan_speed = 20
cmd.swing_mode = 0xF
cmd.beep_on = True
cmd.eco = True
cmd.sleep = True
cmd.turbo = True
Command._message_id = 3
V["set_state_extras"] = {"message_id": 4, "power": True, "target": 26.0, "mode": 4, "fan": 20,
                         "swing": 0xF, "humidity": 40, "beep": True, "eco": True, "sleep": True,
                         "turbo": True, "frame": h(cmd.tobytes())}

for beep in (True, False):
    cmd = ToggleDisplayCommand()
    cmd.beep_on = beep
    Command._message_id = 4
    V["toggle_display" + ("" if beep else "_silent")] = {"message_id": 5, "beep": beep,
                                                         "frame": h(cmd.tobytes())}

# --- Paquete V2 (5A5A) -----------------------------------------------------
device_id = 151732605161920
timestamp = bytes([12, 34, 56, 7, 11, 10, 26, 20])
_Packet._timestamp = classmethod(lambda cls: timestamp)
frame = bytes.fromhex(V["get_state"]["frame"])
packet = _Packet.encode(device_id, frame)
V["v2_packet"] = {"device_id": device_id, "timestamp": h(timestamp),
                  "frame": h(frame), "packet": h(packet),
                  "decoded": h(_Packet.decode(packet))}

# --- Protocolo V3 (8370) ---------------------------------------------------
local_key = bytes(range(100, 132))
pad_bytes = bytes([0xEE] * 16)
lan.get_random_bytes = lambda n: pad_bytes[:n]

p = _LanProtocolV3()
p._local_key = local_key
enc = p._encode_encrypted_request(5, packet)
V["v3_request"] = {"local_key": h(local_key), "packet_id": 5, "pad_bytes": h(pad_bytes),
                   "data": h(packet), "packet": h(enc)}

token = bytes(range(0, 64))
V["v3_handshake"] = {"packet_id": 0, "token": h(token),
                     "packet": h(p._encode_handshake_request(0, token))}

# Respuesta cifrada (tipo 3): misma estructura que la petición, con tipo 3
data = packet
remainder = (len(data) + 2) % 16
pad = 16 - remainder if remainder else 0
header = p._build_header(len(data) + pad + 32, bytes([pad << 4 | 0x3]))
payload = (9).to_bytes(2, "big") + data + pad_bytes[:pad]
resp = header + Security.encrypt_aes_cbc(local_key, payload) + sha256(header + payload).digest()
assert p._process_packet(memoryview(resp)) == data
V["v3_response"] = {"local_key": h(local_key), "packet": h(resp), "data": h(data)}

# Respuesta del handshake -> clave local
cloud_key = bytes(range(200, 232)) if False else bytes((i * 7 + 3) & 0xFF for i in range(32))
secret = bytes((i * 13 + 5) & 0xFF for i in range(32))
hs_data = Security.encrypt_aes_cbc(cloud_key, secret) + sha256(secret).digest()
hs_packet = p._build_header(len(hs_data), bytes([0x1])) + (0).to_bytes(2, "big") + hs_data
hs_payload = p._process_packet(memoryview(hs_packet))
derived = p._get_local_key(cloud_key, memoryview(hs_payload))
V["v3_auth"] = {"key": h(cloud_key), "response_packet": h(hs_packet), "local_key": h(derived)}

# --- udpid -----------------------------------------------------------------
V["udpid"] = {"device_id": device_id,
              "little": h(Security.udpid(device_id.to_bytes(6, "little"))),
              "big": h(Security.udpid(device_id.to_bytes(6, "big")))}

# --- Respuesta de descubrimiento V3 ---------------------------------------
body = bytearray(64)
body[0:4] = bytes([50, 1, 168, 192])           # IP al revés
body[4:6] = (6444).to_bytes(2, "little")
sn = b"000000P0000000Q1A1B2C3D4E5F60000"
body[8:40] = sn
name = b"net_ac_1A2B"
body[40] = len(name)
body[41:41 + len(name)] = name
inner = bytearray(40)
inner[0:2] = b"\x5a\x5a"
inner[20:26] = device_id.to_bytes(6, "little")
inner += Security.encrypt_aes(bytes(body)) + bytes(16)
disc = b"\x83\x70" + bytes(6) + bytes(inner) + bytes(16)
import asyncio
info = asyncio.run(Discover._get_device_info("192.168.1.50", 3, disc))
V["discovery"] = {"response": h(disc), "ip": info["ip"], "port": info["port"],
                  "device_id": info["device_id"], "name": info["name"], "sn": info["sn"],
                  "device_type": info["device_type"], "version": info["version"]}

# --- Respuesta de estado ---------------------------------------------------
state_payload = bytearray(25)
state_payload[0] = 0xC0
state_payload[1] = 0x01                       # encendido
state_payload[2] = (2 << 5) | 0x10 | (23 - 16)  # frío, 23.5
state_payload[3] = 60                         # ventilador media
state_payload[7] = 0x0C
state_payload[9] = 0x10                       # eco
state_payload[11] = 50 + 2 * 24               # dentro 24
state_payload[12] = 50 + 2 * 31               # fuera 31
state_payload[15] = 0x23                      # decimales: dentro .3, fuera .2
state_payload[14] = 0x00
state_payload[19] = 45
state_payload[24] = 0x00
state_payload = bytes(state_payload) + bytes([crc8.calculate(state_payload)])
frame_hdr = bytearray(10)
frame_hdr[0] = 0xAA
frame_hdr[1] = len(state_payload) + 10
frame_hdr[2] = 0xAC
frame_hdr[9] = 0x03
state_frame = bytes(frame_hdr) + state_payload
state_frame += bytes([Frame.checksum(state_frame[1:])])
r = Response.construct(state_frame)
assert isinstance(r, StateResponse)
V["state"] = {"frame": h(state_frame), "power": r.power_on, "target": r.target_temperature,
              "mode": r.operational_mode, "fan": r.fan_speed, "swing": r.swing_mode,
              "eco": r.eco, "turbo": r.turbo, "indoor": r.indoor_temperature,
              "outdoor": r.outdoor_temperature, "humidity": r.target_humidity}

# Pantalla apagada, filtro sucio, sleep, error y oscilación en ambas direcciones
extra = bytearray(bytes.fromhex(V["state"]["frame"])[10:-2])
extra[7] = 0x0F
extra[10] = 0x01                              # sleep
extra[13] = 0x20                              # aviso de filtro
extra[14] = 0x70                              # pantalla apagada
extra[16] = 0x05                              # código de error
extra = bytes(extra) + bytes([crc8.calculate(extra)])
extra_frame = bytes(frame_hdr[:1]) + bytes([len(extra) + 10]) + bytes(frame_hdr[2:]) + extra
extra_frame += bytes([Frame.checksum(extra_frame[1:])])
r = Response.construct(extra_frame)
assert isinstance(r, StateResponse)
V["state_extras"] = {"frame": h(extra_frame), "swing": r.swing_mode, "sleep": r.sleep,
                     "filter_alert": r.filter_alert, "display_on": r.display_on,
                     "error_code": r.error_code}

# --- Nube SmartHome --------------------------------------------------------
sec = SmartHomeCloud._Security()
V["cloud"] = {"login_id": "abc123", "password": "Contraseña1".encode("ascii", "ignore").decode(),
              "json": '{"a": 1}', "random": "00112233445566778899aabbccddeeff"}
V["cloud"]["sign"] = sec.sign(V["cloud"]["json"], V["cloud"]["random"])
V["cloud"]["password_hash"] = sec.encrypt_password("abc123", V["cloud"]["password"])
V["cloud"]["iam_hash"] = sec.encrypt_iam_password("abc123", V["cloud"]["password"])

print(json.dumps(V, indent=2))
