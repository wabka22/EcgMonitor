// #include <WiFi.h>
// #include <WiFiClient.h>
// #include <WiFiAP.h>
// #include <Preferences.h>
// #include <math.h>

// Preferences prefs;
// WiFiServer server(8888);

// unsigned long lastConnectionAttempt = 0;
// const unsigned long CONNECTION_RETRY_INTERVAL = 30000;
// bool isConnecting = false;
// bool isStreaming = false;

// String savedSSID = "";
// String savedPass = "";

// // -------------------- Косинус --------------------
// float cosineTime = 0.0;
// const float SAMPLE_RATE = 100.0;
// const float FREQUENCY = 1.0;
// const float AMPLITUDE = 100.0;
// const float OFFSET = AMPLITUDE;
// unsigned long lastSampleTime = 0;
// const unsigned long SAMPLE_INTERVAL = (unsigned long)(1000.0 / SAMPLE_RATE);

// WiFiClient activeClient;

// void connectToWiFi();
// void handleWiFiReconnection();
// void printNetworkStatus(WiFiClient& client);
// void streamCosineWaveTick();

// // -------------------- SETUP ------------------------
// void setup() {
//   Serial.begin(115200);
//   delay(1000); // Даем время для стабилизации
  
//   Serial.println("\n\n=== ESP32 WiFi AP+STA Setup ===");
  
//   // Инициализируем Preferences
//   if (!prefs.begin("wifi", false)) {
//     Serial.println("ERROR: Failed to initialize Preferences!");
//   } else {
//     Serial.println("Preferences initialized OK");
//   }
  
//   // Читаем сохраненные данные
//   savedSSID = prefs.getString("ssid", "");
//   savedPass = prefs.getString("pass", "");
  
//   Serial.printf("Read from NVS - SSID: '%s', PASS: '%s'\n", 
//                 savedSSID.c_str(), savedPass.c_str());
  
//   // Сначала включаем только AP режим
//   WiFi.mode(WIFI_AP);
//   Serial.println("Setting up AP mode...");
  
//   if (!WiFi.softAP("karch_cos_88005553535", "12345678")) {
//     Serial.println("ERROR: Failed to setup AP!");
//   } else {
//     Serial.print("AP IP: ");
//     Serial.println(WiFi.softAPIP());
//   }
  
//   // Если есть сохраненные данные - включаем STA режим и пытаемся подключиться
//   if (savedSSID.length() > 0) {
//     Serial.println("Saved WiFi credentials found, enabling STA mode...");
//     WiFi.mode(WIFI_AP_STA);
//     connectToWiFi();
//   } else {
//     Serial.println("No saved WiFi credentials. Only AP mode active.");
//   }
  
//   server.begin();
//   Serial.println("TCP server started on port 8888");
//   Serial.println("Ready for commands...");
// }

// // -------------------- WiFi connect ------------------------
// void connectToWiFi() {
//   if (savedSSID == "" || isConnecting) {
//     Serial.println("connectToWiFi: skipped - no SSID or already connecting");
//     return;
//   }

//   isConnecting = true;
//   Serial.printf("Attempting to connect to: %s\n", savedSSID.c_str());
  
//   // Явно задаем режим перед подключением
//   if (WiFi.getMode() != WIFI_AP_STA) {
//     WiFi.mode(WIFI_AP_STA);
//   }
  
//   WiFi.begin(savedSSID.c_str(), savedPass.c_str());
//   lastConnectionAttempt = millis();
  
//   Serial.println("WiFi.begin() called");
// }

// // -------------------- WiFi reconnect handler ------------------------
// void handleWiFiReconnection() {
//   static unsigned long lastCheck = 0;
  
//   // Проверяем не чаще чем раз в секунду
//   if (millis() - lastCheck < 1000) return;
//   lastCheck = millis();
  
//   wl_status_t status = WiFi.status();
  
//   // Если мы в процессе подключения
//   if (isConnecting) {
//     if (status == WL_CONNECTED) {
//       Serial.println("WiFi Connected!");
//       Serial.printf("IP Address: %s\n", WiFi.localIP().toString().c_str());
//       isConnecting = false;
//     } 
//     else if (millis() - lastConnectionAttempt > 15000) {
//       Serial.printf("Connection failed. Status: %d\n", status);
//       isConnecting = false;
//     } else {
//       // Показываем прогресс каждые 3 секунды
//       static unsigned long lastProgress = 0;
//       if (millis() - lastProgress > 3000) {
//         lastProgress = millis();
//         Serial.printf("Connecting... Status: %d\n", status);
//       }
//     }
//   }
  
//   // Если не подключены и не пытаемся подключиться
//   else if (status != WL_CONNECTED) {
//     if (savedSSID != "" && millis() - lastConnectionAttempt >= CONNECTION_RETRY_INTERVAL) {
//       Serial.println("Attempting reconnection...");
//       connectToWiFi();
//     }
//   }
// }

// // -------------------- STATUS ------------------------
// void printNetworkStatus(WiFiClient& client) {
//   client.println("=== ESP32 Network Status ===");
//   client.print("AP IP: "); client.println(WiFi.softAPIP());
  
//   wl_status_t status = WiFi.status();
//   client.print("WiFi Status: "); 
//   client.println(status == WL_CONNECTED ? "CONNECTED" : 
//                  status == WL_CONNECT_FAILED ? "CONNECT_FAILED" :
//                  status == WL_CONNECTION_LOST ? "CONNECTION_LOST" :
//                  status == WL_DISCONNECTED ? "DISCONNECTED" :
//                  status == WL_IDLE_STATUS ? "IDLE_STATUS" :
//                  status == WL_NO_SSID_AVAIL ? "NO_SSID_AVAIL" :
//                  status == WL_SCAN_COMPLETED ? "SCAN_COMPLETED" :
//                  "UNKNOWN");
  
//   if (status == WL_CONNECTED) {
//     client.print("SSID: "); client.println(WiFi.SSID());
//     client.print("RSSI: "); client.println(WiFi.RSSI());
//     client.print("IP: "); client.println(WiFi.localIP());
//   }
  
//   client.print("Saved SSID in NVS: "); client.println(savedSSID);
//   client.println("=============================");
// }

// // -------------------- STREAMING ------------------------
// void streamCosineWaveTick() {
//   if (!activeClient.connected()) {
//     Serial.println("Stream client disconnected");
//     isStreaming = false;
//     return;
//   }

//   if (activeClient.available()) {
//     String cmd = activeClient.readStringUntil('\n');
//     cmd.trim();
//     if (cmd == "STOP_STREAM") {
//       activeClient.println("OK");
//       activeClient.flush();
//       delay(50);
//       activeClient.stop();
//       isStreaming = false;
//       Serial.println("Stream stopped by client");
//       return;
//     }
//   }

//   unsigned long now = millis();
//   if (now - lastSampleTime < SAMPLE_INTERVAL) return;
//   lastSampleTime = now;

//   float value = AMPLITUDE * cos(2 * PI * FREQUENCY * cosineTime) + OFFSET;
//   cosineTime += 1.0 / SAMPLE_RATE;
//   if (cosineTime > 100000) cosineTime = 0;

//   activeClient.println(String(value, 2));
//   yield();
// }

// // -------------------- MAIN LOOP ------------------------
// void loop() {
//   handleWiFiReconnection();

//   if (isStreaming) {
//     streamCosineWaveTick();
//     return;
//   }

//   WiFiClient client = server.available();
//   if (!client) return;

//   Serial.println("New client connected");
//   client.setTimeout(5000);

//   String command = client.readStringUntil('\n');
//   command.trim();
//   Serial.printf("Received command: %s\n", command.c_str());

//   // =================== SET SSID/PASS ===================
//   if (command == "SET") {
//     String ssid = client.readStringUntil('\n');
//     String pass = client.readStringUntil('\n');
//     ssid.trim();
//     pass.trim();

//     Serial.printf("SET command - SSID: '%s', PASS: '%s'\n", ssid.c_str(), pass.c_str());

//     if (ssid.length() > 0 && pass.length() > 0) {
//       // Сохраняем в NVS
//       if (prefs.putString("ssid", ssid) && prefs.putString("pass", pass)) {
//         Serial.println("Credentials saved to NVS");
//         savedSSID = ssid;
//         savedPass = pass;
        
//         // Отправляем ответ и закрываем соединение
//         client.println("OK");
//         client.flush();
//         delay(100);
//         client.stop();
        
//         // Переподключаемся
//         Serial.println("Reconnecting with new credentials...");
//         if (WiFi.getMode() != WIFI_AP_STA) {
//           WiFi.mode(WIFI_AP_STA);
//         }
//         connectToWiFi();
//       } else {
//         client.println("ERROR: Failed to save credentials");
//         client.flush();
//         delay(50);
//         client.stop();
//       }
//     } else {
//       client.println("ERROR: Invalid SSID or password");
//       client.flush();
//       delay(50);
//       client.stop();
//     }
//     return;
//   }

//   // =================== STATUS ===================
//   else if (command == "STATUS") {
//     printNetworkStatus(client);
//     client.flush();
//     delay(50);
//     client.stop();
//     return;
//   }

//   // =================== FORCE_RECONNECT ===================
//   else if (command == "FORCE_RECONNECT") {
//     Serial.println("Force reconnection requested");
//     client.println("OK");
//     client.flush();
//     delay(50);
//     client.stop();
    
//     if (savedSSID.length() > 0) {
//       connectToWiFi();
//     } else {
//       Serial.println("Cannot reconnect: no saved credentials");
//     }
//     return;
//   }

//   // =================== STREAM ===================
//   else if (command == "START_STREAM") {
//     Serial.println("Starting stream");
//     client.println("OK");
//     client.flush();
//     isStreaming = true;
//     activeClient = client;
//     cosineTime = 0;
//     lastSampleTime = millis();
//     return;
//   }

//   // =================== CLEAR ===================
//   else if (command == "CLEAR") {
//     Serial.println("Clearing WiFi credentials");
//     prefs.remove("ssid");
//     prefs.remove("pass");
//     savedSSID = "";
//     savedPass = "";
//     WiFi.disconnect(true);
//     WiFi.mode(WIFI_AP); // Возвращаемся только в AP режим
//     client.println("OK: Credentials cleared");
//     client.flush();
//     delay(50);
//     client.stop();
//     return;
//   }

//   // =================== UNKNOWN ===================
//   else {
//     Serial.printf("Unknown command: %s\n", command.c_str());
//     client.println("ERROR: Unknown command");
//     client.flush();
//     delay(50);
//     client.stop();
//     return;
//   }
// }