import subprocess
import platform
import time
import json
import sys
import socket
import tempfile
import os
from datetime import datetime

CONFIG_FILE = "config.json"

# ---------- ЛОГИРОВАНИЕ ----------
def log(msg, level="INFO"):
    ts = datetime.now().strftime("%H:%M:%S")
    colors = {
        "INFO": "\033[94m",
        "SUCCESS": "\033[92m",
        "WARN": "\033[93m",
        "ERROR": "\033[91m"
    }
    color = colors.get(level, "\033[0m")
    print(f"{color}[{ts}] [{level}] {msg}\033[0m")

# ---------- ЗАГРУЗКА КОНФИГА ----------
def load_config():
    try:
        with open(CONFIG_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        log(f"Файл {CONFIG_FILE} не найден. Создайте его с Wi-Fi данными.", "ERROR")
        sys.exit(1)
    except json.JSONDecodeError:
        log(f"Ошибка: {CONFIG_FILE} повреждён или содержит неверный JSON.", "ERROR")
        sys.exit(1)

# ---------- СКАНИРОВАНИЕ Wi-Fi ----------
def scan_networks():
    os_name = platform.system()
    try:
        if os_name == "Windows":
            result = subprocess.run(["netsh", "wlan", "show", "networks", "mode=Bssid"],
                                    capture_output=True, text=True, encoding="cp866")
        else:
            result = subprocess.run(["nmcli", "-t", "-f", "SSID", "dev", "wifi"],
                                    capture_output=True, text=True)

        if result.returncode != 0 or not result.stdout:
            return []

        networks = []
        for line in result.stdout.splitlines():
            if os_name == "Windows":
                if "SSID" in line:
                    ssid = line.split(":", 1)[1].strip()
                    if ssid:
                        networks.append(ssid)
            else:
                if line.strip():
                    networks.append(line.strip())
        return list(set(networks))
    except Exception as e:
        log(f"Ошибка при сканировании Wi-Fi: {e}", "ERROR")
        return []

# ---------- АВТОМАТИЧЕСКОЕ ПОДКЛЮЧЕНИЕ ----------
def connect_to_network(ssid, password):
    os_name = platform.system()
    log(f"Подключение к сети {ssid}...", "INFO")

    try:
        if os_name == "Windows":
            # создаём временный XML-профиль
            wifi_profile = f"""<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>
"""
            with tempfile.NamedTemporaryFile(delete=False, suffix=".xml", mode="w", encoding="utf-8") as f:
                f.write(wifi_profile)
                temp_path = f.name

            subprocess.run(["netsh", "wlan", "add", "profile", f"filename={temp_path}"], check=False)
            subprocess.run(["netsh", "wlan", "connect", f"name={ssid}"], check=False)
            os.remove(temp_path)
        else:
            subprocess.run(["nmcli", "d", "wifi", "connect", ssid, "password", password], check=False)

        time.sleep(5)
        log(f"Подключение к {ssid} выполнено (или в процессе)...", "SUCCESS")
    except Exception as e:
        log(f"Ошибка подключения: {e}", "ERROR")

# ---------- ОТПРАВКА ДАННЫХ НА ESP ----------
def send_wifi_credentials_to_esp(pc_ssid, pc_password):
    log("Отправка данных на ESP...", "INFO")
    esp_ip = "192.168.4.1"
    esp_port = 8888
    try:
        with socket.create_connection((esp_ip, esp_port), timeout=10) as s:
            data = f"SET\n{pc_ssid}\n{pc_password}\n"
            s.sendall(data.encode("utf-8"))
            response = s.recv(1024).decode("utf-8", errors="ignore")
            log(f"Ответ от ESP: {response.strip()}", "SUCCESS")
    except Exception as e:
        log(f"Ошибка при отправке данных на ESP: {e}", "ERROR")

# ---------- ОСНОВНОЙ ЦИКЛ ----------
def main():
    config = load_config()
    esp_name = config["esp_network_name"]
    esp_pass = config["esp_network_password"]
    pc_ssid = config["pc_wifi_ssid"]
    pc_pass = config["pc_wifi_password"]

    log("=== ESP32 Auto-Connector ===", "INFO")

    while True:
        networks = scan_networks()
        if not networks:
            log("Нет доступных сетей. Повтор через 5 секунд...", "WARN")
            time.sleep(5)
            continue

        if esp_name in networks:
            log(f"Обнаружена ESP-сеть: {esp_name}", "SUCCESS")
            connect_to_network(esp_name, esp_pass)
            send_wifi_credentials_to_esp(pc_ssid, pc_pass)
            log("Ожидание 10 секунд перед повторной проверкой...", "INFO")
            time.sleep(10)
        else:
            log(f"ESP-сеть '{esp_name}' не найдена. Повтор через 5 секунд...", "WARN")
            time.sleep(5)

if __name__ == "__main__":
    main()
