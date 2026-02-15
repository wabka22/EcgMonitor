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

float cosineTime = 0.0;
const float SAMPLE_RATE = 100.0;
const float FREQUENCY = 1.0;
unsigned long lastSampleTime = 0;
const unsigned long SAMPLE_INTERVAL = (unsigned long)(1000.0 / SAMPLE_RATE);

WiFiClient activeClient;

const int LED_PIN = 2;

enum LEDState {
  LED_OFF = 0,
  LED_ON = 1,
  LED_SLOW_BLINK = 2,
  LED_FAST_BLINK = 3,
  LED_VERY_FAST_BLINK = 4
};

LEDState currentLedState = LED_SLOW_BLINK;
unsigned long lastLedToggle = 0;
int ledOnDuration = 0;
int ledOffDuration = 0;

void connectToWiFi();
void handleWiFiReconnection();
void printNetworkStatus(WiFiClient& client);
void streamCosineWaveTick();
void updateLED();
void setLEDState(LEDState state);

void setup() {
  Serial.begin(115200);
  delay(1000);
  
  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);
  
  Serial.println("\n\n=== ESP32 WiFi AP Setup ===");
  
  if (!prefs.begin("wifi", false)) {
    Serial.println("ERROR: Failed to initialize Preferences!");
    setLEDState(LED_VERY_FAST_BLINK);
  } else {
    Serial.println("Preferences initialized OK");
  }
  
  WiFi.mode(WIFI_AP);
  Serial.println("Setting up AP mode...");
  
  if (!WiFi.softAP("ESP32_Cos_Streamer", "12345678")) {
    Serial.println("ERROR: Failed to setup AP!");
    setLEDState(LED_VERY_FAST_BLINK);
  } else {
    Serial.print("AP IP: ");
    Serial.println(WiFi.softAPIP());
    setLEDState(LED_SLOW_BLINK);
  }
  
  server.begin();
  Serial.println("TCP server started on port 8888");
  Serial.println("Ready for commands...");
}

void connectToWiFi() {
  if (savedSSID == "" || isConnecting) {
    return;
  }

  isConnecting = true;
  Serial.printf("Attempting to connect to: %s\n", savedSSID.c_str());
  setLEDState(LED_FAST_BLINK);
  
  WiFi.mode(WIFI_AP_STA);
  WiFi.begin(savedSSID.c_str(), savedPass.c_str());
  lastConnectionAttempt = millis();
  
  Serial.println("WiFi.begin() called");
}

void handleWiFiReconnection() {
  static unsigned long lastCheck = 0;
  
  if (millis() - lastCheck < 1000) return;
  lastCheck = millis();
  
  wl_status_t status = WiFi.status();
  
  if (isConnecting) {
    if (status == WL_CONNECTED) {
      Serial.println("WiFi Connected!");
      Serial.printf("IP Address: %s\n", WiFi.localIP().toString().c_str());
      isConnecting = false;
      setLEDState(LED_ON);
    } 
    else if (millis() - lastConnectionAttempt > 15000) {
      Serial.printf("Connection failed. Status: %d\n", status);
      isConnecting = false;
      setLEDState(LED_SLOW_BLINK);
    }
  }
  else if (status != WL_CONNECTED) {
    if (savedSSID != "" && millis() - lastConnectionAttempt >= CONNECTION_RETRY_INTERVAL) {
      Serial.println("Attempting reconnection...");
      connectToWiFi();
    }
  }
}

void setLEDState(LEDState state) {
  currentLedState = state;
  lastLedToggle = millis();
  
  switch(state) {
    case LED_OFF:
      digitalWrite(LED_PIN, LOW);
      ledOnDuration = 0;
      ledOffDuration = 0;
      break;
    case LED_ON:
      digitalWrite(LED_PIN, HIGH);
      ledOnDuration = 0;
      ledOffDuration = 0;
      break;
    case LED_SLOW_BLINK:
      digitalWrite(LED_PIN, HIGH);
      ledOnDuration = 500;
      ledOffDuration = 500;
      break;
    case LED_FAST_BLINK:
      digitalWrite(LED_PIN, HIGH);
      ledOnDuration = 100;
      ledOffDuration = 100;
      break;
    case LED_VERY_FAST_BLINK:
      digitalWrite(LED_PIN, HIGH);
      ledOnDuration = 50;
      ledOffDuration = 50;
      break;
  }
}

void updateLED() {
  if (ledOnDuration == 0 || ledOffDuration == 0) {
    return;
  }
  
  unsigned long now = millis();
  unsigned long elapsed = now - lastLedToggle;
  
  bool isCurrentlyOn = digitalRead(LED_PIN) == HIGH;
  
  if (isCurrentlyOn && elapsed >= ledOnDuration) {
    digitalWrite(LED_PIN, LOW);
    lastLedToggle = now;
  } 
  else if (!isCurrentlyOn && elapsed >= ledOffDuration) {
    digitalWrite(LED_PIN, HIGH);
    lastLedToggle = now;
  }
}

void printNetworkStatus(WiFiClient& client) {
  client.println("=== ESP32 Network Status ===");
  client.print("AP IP: "); client.println(WiFi.softAPIP());
  
  wl_status_t status = WiFi.status();
  client.print("WiFi Status: "); 
  client.println(status == WL_CONNECTED ? "CONNECTED" : 
                 status == WL_CONNECT_FAILED ? "CONNECT_FAILED" :
                 status == WL_CONNECTION_LOST ? "CONNECTION_LOST" :
                 status == WL_DISCONNECTED ? "DISCONNECTED" :
                 status == WL_IDLE_STATUS ? "IDLE_STATUS" :
                 status == WL_NO_SSID_AVAIL ? "NO_SSID_AVAIL" :
                 status == WL_SCAN_COMPLETED ? "SCAN_COMPLETED" :
                 "UNKNOWN");
  
  if (status == WL_CONNECTED) {
    client.print("SSID: "); client.println(WiFi.SSID());
    client.print("RSSI: "); client.println(WiFi.RSSI());
    client.print("IP: "); client.println(WiFi.localIP());
  }
  
  client.print("Saved SSID: "); client.println(savedSSID);
  
  client.print("LED State: ");
  switch(currentLedState) {
    case LED_OFF: client.println("OFF"); break;
    case LED_ON: client.println("ON (WiFi Connected)"); break;
    case LED_SLOW_BLINK: client.println("SLOW BLINK (AP Mode Only)"); break;
    case LED_FAST_BLINK: client.println("FAST BLINK (Connecting...)"); break;
    case LED_VERY_FAST_BLINK: client.println("VERY FAST BLINK (Error)"); break;
  }
  
  client.println("=============================");
}

void streamCosineWaveTick() {
  if (!activeClient.connected()) {
    Serial.println("Stream client disconnected");
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
      Serial.println("Stream stopped by client");
      return;
    }
  }

  unsigned long now = millis();
  if (now - lastSampleTime < SAMPLE_INTERVAL) return;
  lastSampleTime = now;

  float normalizedValue = cos(2 * PI * FREQUENCY * cosineTime);
  cosineTime += 1.0 / SAMPLE_RATE;
  if (cosineTime > 100000) cosineTime = 0;

  activeClient.println(String(normalizedValue, 3));
  
  yield();
}

void loop() {
  handleWiFiReconnection();
  updateLED();

  if (isStreaming) {
    streamCosineWaveTick();
    return;
  }

  WiFiClient client = server.available();
  if (!client) return;

  Serial.println("New client connected");
  client.setTimeout(5000);

  String command = client.readStringUntil('\n');
  command.trim();
  Serial.printf("Received command: %s\n", command.c_str());

  if (command == "SET") {
    String ssid = client.readStringUntil('\n');
    String pass = client.readStringUntil('\n');
    ssid.trim();
    pass.trim();

    Serial.printf("SET command - SSID: '%s', PASS: '%s'\n", ssid.c_str(), pass.c_str());

    if (ssid.length() > 0 && pass.length() > 0) {
      if (prefs.putString("ssid", ssid) && prefs.putString("pass", pass)) {
        Serial.println("Credentials saved to NVS");
        savedSSID = ssid;
        savedPass = pass;
        
        client.println("OK");
        client.flush();
        delay(100);
        client.stop();
        
        Serial.println("Attempting to connect with new credentials...");
        setLEDState(LED_FAST_BLINK);
        connectToWiFi();
      } else {
        client.println("ERROR: Failed to save credentials");
        client.flush();
        delay(50);
        client.stop();
        setLEDState(LED_VERY_FAST_BLINK);
      }
    } else {
      client.println("ERROR: Invalid SSID or password");
      client.flush();
      delay(50);
      client.stop();
    }
    return;
  }

  else if (command == "STATUS") {
    printNetworkStatus(client);
    client.flush();
    delay(50);
    client.stop();
    return;
  }

  else if (command == "START_STREAM") {
    Serial.println("Starting cosine wave stream");
    client.println("OK");
    client.flush();
    isStreaming = true;
    activeClient = client;
    cosineTime = 0;
    lastSampleTime = millis();
    setLEDState(LED_ON);
    return;
  }

  else if (command == "CLEAR") {
    Serial.println("Clearing WiFi credentials");
    prefs.remove("ssid");
    prefs.remove("pass");
    savedSSID = "";
    savedPass = "";
    WiFi.disconnect(true);
    WiFi.mode(WIFI_AP);
    client.println("OK: Credentials cleared");
    client.flush();
    delay(50);
    client.stop();
    setLEDState(LED_SLOW_BLINK);
    return;
  }

  else {
    Serial.printf("Unknown command: %s\n", command.c_str());
    client.println("ERROR: Unknown command");
    client.flush();
    delay(50);
    client.stop();
    return;
  }
}