#include <WiFi.h>
#include <WiFiClient.h>
#include <WiFiAP.h>
#include <Preferences.h>
#include <math.h>

Preferences prefs;
WiFiServer server(8888);

unsigned long lastConnectionAttempt = 0;
const unsigned long CONNECTION_RETRY_INTERVAL = 30000;
bool isConnecting = false;
bool isStreaming = false;

String savedSSID = "";
String savedPass = "";

// -------------------- Косинус --------------------
float cosineTime = 0.0;
const float SAMPLE_RATE = 100.0;
const float FREQUENCY = 1.0;
const float AMPLITUDE = 100.0;
const float OFFSET = AMPLITUDE;
unsigned long lastSampleTime = 0;
const unsigned long SAMPLE_INTERVAL = (unsigned long)(1000.0 / SAMPLE_RATE);

WiFiClient activeClient;

void connectToWiFi();
void handleWiFiReconnection();
void printNetworkStatus(WiFiClient& client);
void streamCosineWaveTick();

// -------------------- SETUP ------------------------
void setup() {
  Serial.begin(115200);
  prefs.begin("wifi", false);

  savedSSID = prefs.getString("ssid", "");
  savedPass = prefs.getString("pass", "");

  WiFi.mode(WIFI_AP_STA);
  WiFi.softAP("karch_cos_88005553535", "12345678");

  Serial.print("AP IP: ");
  Serial.println(WiFi.softAPIP());

  if (savedSSID != "") connectToWiFi();

  server.begin();
}

// -------------------- WiFi connect ------------------------
void connectToWiFi() {
  if (savedSSID == "" || isConnecting) return;

  isConnecting = true;
  Serial.printf("Connecting to Wi-Fi: %s\n", savedSSID.c_str());
  WiFi.begin(savedSSID.c_str(), savedPass.c_str());
  lastConnectionAttempt = millis();
}

// -------------------- WiFi reconnect handler ------------------------
void handleWiFiReconnection() {
  if (WiFi.status() != WL_CONNECTED && !isConnecting) {
    if (millis() - lastConnectionAttempt >= CONNECTION_RETRY_INTERVAL) {
      if (savedSSID != "") connectToWiFi();
    }
  }

  if (isConnecting) {
    if (WiFi.status() == WL_CONNECTED) {
      Serial.println("Connected!");
      isConnecting = false;
    } 
    else if (millis() - lastConnectionAttempt > 15000) {
      Serial.println("Failed. Retry later.");
      isConnecting = false;
    }
  }
}

// -------------------- STATUS ------------------------
void printNetworkStatus(WiFiClient& client) {
  client.println("=== ESP32 Network Status ===");
  client.print("AP IP: "); client.println(WiFi.softAPIP());
  client.print("Connected to Wi-Fi: ");
  client.println(WiFi.status() == WL_CONNECTED ? "YES" : "NO");

  if (WiFi.status() == WL_CONNECTED) {
    client.print("SSID: "); client.println(WiFi.SSID());
    client.print("IP: "); client.println(WiFi.localIP());
  }
  client.println("=============================");
}

// -------------------- STREAMING ------------------------
void streamCosineWaveTick() {
  if (!activeClient.connected()) {
    isStreaming = false;
    return;
  }

  if (activeClient.available()) {
    String cmd = activeClient.readStringUntil('\n');
    cmd.trim();
    if (cmd == "STOP_STREAM") {
      activeClient.println("OK");
      activeClient.flush();
      delay(50);
      activeClient.stop();
      isStreaming = false;
      return;
    }
  }

  unsigned long now = millis();
  if (now - lastSampleTime < SAMPLE_INTERVAL) return;
  lastSampleTime = now;

  float value = AMPLITUDE * cos(2 * PI * FREQUENCY * cosineTime) + OFFSET;
  cosineTime += 1.0 / SAMPLE_RATE;
  if (cosineTime > 100000) cosineTime = 0;

  activeClient.println(String(value, 2));
  yield();
}

// -------------------- MAIN LOOP ------------------------
void loop() {
  handleWiFiReconnection();

  if (isStreaming) {
    streamCosineWaveTick();
    return;
  }

  WiFiClient client = server.available();
  if (!client) return;

  Serial.println("Client connected.");
  client.setTimeout(5000);

  String command = client.readStringUntil('\n');
  command.trim();

  // =================== SET SSID/PASS ===================
  if (command == "SET") {

    String ssid = client.readStringUntil('\n');
    String pass = client.readStringUntil('\n');
    ssid.trim();
    pass.trim();

    Serial.printf("Got SSID=%s PASS=%s\n", ssid.c_str(), pass.c_str());

    if (ssid.length() > 0 && pass.length() > 0) {
      prefs.putString("ssid", ssid);
      prefs.putString("pass", pass);
      savedSSID = ssid;
      savedPass = pass;

      // ---------- ВАЖНО ----------  
      // СНАЧАЛА отправляем ответ  
      client.println("OK");
      client.flush();
      delay(50);
      client.stop();      // <-- ГАРАНТИРОВАНОЕ закрытие сокета

      // ---------- Подключаемся ТОЛЬКО ПОСЛЕ ЗАКРЫТИЯ ----------
      connectToWiFi();
      return;
    } else {
      client.println("ERROR");
      client.flush();
      delay(50);
      client.stop();
      return;
    }
  }

  // =================== STATUS ===================
  else if (command == "STATUS") {
    printNetworkStatus(client);
    client.flush();
    delay(50);
    client.stop();
    return;
  }

  // =================== FORCE_RECONNECT ===================
  else if (command == "FORCE_RECONNECT") {
    client.println("OK");
    client.flush();
    delay(50);
    client.stop();
    connectToWiFi();
    return;
  }

  // =================== STREAM ===================
  else if (command == "START_STREAM") {
    client.println("OK");
    client.flush();
    isStreaming = true;
    activeClient = client;
    cosineTime = 0;
    lastSampleTime = millis();
    return;
  }

  // =================== UNKNOWN ===================
  else {
    client.println("ERROR");
    client.flush();
    delay(50);
    client.stop();
    return;
  }
}
