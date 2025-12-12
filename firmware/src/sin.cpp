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

// // Параметры синусоиды - переименовал переменную
// float sineTime = 0.0;  // было: time
// const float SAMPLE_RATE = 100.0; // Гц
// const float FREQUENCY = 1.0;     // Гц
// const float AMPLITUDE = 100.0;   // амплитуда

// void connectToWiFi();
// void handleWiFiReconnection();
// void printNetworkStatus(WiFiClient& client);
// void streamSineWave(WiFiClient& client);

// void setup() {
//   Serial.begin(115200);
//   prefs.begin("wifi", false);

//   savedSSID = prefs.getString("ssid", "");
//   savedPass = prefs.getString("pass", "");

//   WiFi.mode(WIFI_AP_STA);
//   WiFi.softAP("karch_sin_88005553535", "12345678");

//   Serial.print("Access Point started. IP: ");
//   Serial.println(WiFi.softAPIP());

//   if (savedSSID != "") {
//     connectToWiFi();
//   } else {
//     Serial.println("No saved Wi-Fi credentials. Waiting for setup...");
//   }

//   server.begin();
//   Serial.println("TCP server started on port 8888.");
// }

// void connectToWiFi() {
//   if (savedSSID == "" || isConnecting) return;

//   Serial.printf("Attempting to connect to Wi-Fi: %s\n", savedSSID.c_str());
//   isConnecting = true;

//   WiFi.begin(savedSSID.c_str(), savedPass.c_str());
//   lastConnectionAttempt = millis();
// }

// void handleWiFiReconnection() {
//   if (WiFi.status() != WL_CONNECTED && !isConnecting) {
//     if (millis() - lastConnectionAttempt >= CONNECTION_RETRY_INTERVAL) {
//       if (savedSSID != "") {
//         Serial.println("Reconnecting to Wi-Fi...");
//         connectToWiFi();
//       }
//     }
//   }

//   if (isConnecting) {
//     if (WiFi.status() == WL_CONNECTED) {
//       Serial.println("\n✅ Connected to Wi-Fi!");
//       Serial.print("Local IP: ");
//       Serial.println(WiFi.localIP());
//       isConnecting = false;
//     } else if (millis() - lastConnectionAttempt > 15000) {
//       Serial.println("\n❌ Failed to connect. Will retry in 30s.");
//       isConnecting = false;
//     }
//   }
// }

// void printNetworkStatus(WiFiClient& client) {
//   client.println("=== ESP32 Network Status ===");
//   client.print("AP SSID: "); client.println("karch_sin_88005553535");
//   client.print("AP IP: "); client.println(WiFi.softAPIP());
//   client.print("Connected to external Wi-Fi: ");
//   client.println(WiFi.status() == WL_CONNECTED ? "YES" : "NO");

//   if (WiFi.status() == WL_CONNECTED) {
//     client.print("Wi-Fi SSID: "); client.println(WiFi.SSID());
//     client.print("Local IP: "); client.println(WiFi.localIP());
//   }
//   client.println("=============================");
// }

// void streamSineWave(WiFiClient& client) {
//   isStreaming = true;
//   unsigned long lastSampleTime = 0;
//   const unsigned long SAMPLE_INTERVAL = 1000 / SAMPLE_RATE; // мс
  
//   Serial.println("Starting sine wave streaming...");
  
//   while (isStreaming && client.connected()) {
//     unsigned long currentTime = millis();
    
//     if (currentTime - lastSampleTime >= SAMPLE_INTERVAL) {
//       // Генерация синусоидального сигнала - используем sineTime вместо time
//       float value = AMPLITUDE * sin(2 * PI * FREQUENCY * sineTime) + AMPLITUDE;
      
//       // Отправка данных клиенту
//       client.println(String(value, 2));
      
//       // Вывод в Serial для отладки
//       Serial.printf("Sine value: %.2f\n", value);
      
//       // Обновляем sineTime вместо time
//       sineTime += 1.0 / SAMPLE_RATE;
//       lastSampleTime = currentTime;
//     }
    
//     // Проверка команды остановки от клиента
//     if (client.available()) {
//       String command = client.readStringUntil('\n');
//       command.trim();
//       if (command == "STOP_STREAM") {
//         Serial.println("Stop stream command received");
//         isStreaming = false;
//         break;
//       }
//     }
    
//     delay(1); // Небольшая задержка для стабильности
//   }
  
//   isStreaming = false;
//   Serial.println("Streaming stopped");
// }

// void loop() {
//   handleWiFiReconnection();

//   WiFiClient client = server.available();
//   if (client) {
//     Serial.println("Client connected.");
//     client.setTimeout(5000);

//     String command = client.readStringUntil('\n');
//     command.trim();

//     if (command.startsWith("SET")) {
//       String ssid = client.readStringUntil('\n');
//       String pass = client.readStringUntil('\n');
//       ssid.trim();
//       pass.trim();

//       if (ssid.length() > 0 && pass.length() > 0) {
//         Serial.printf("Received credentials:\nSSID: %s\nPASS: %s\n",
//                       ssid.c_str(), pass.c_str());
//         prefs.putString("ssid", ssid);
//         prefs.putString("pass", pass);
//         savedSSID = ssid;
//         savedPass = pass;

//         client.println("OK: Credentials saved. Connecting...");
//         connectToWiFi();
//       } else {
//         client.println("ERROR: Invalid credentials format.");
//       }
//     } else if (command == "STATUS") {
//       printNetworkStatus(client);
//     } else if (command == "FORCE_RECONNECT") {
//       client.println("Reconnecting...");
//       connectToWiFi();
//     } else if (command == "START_STREAM") {
//       client.println("OK: Starting data stream");
//       streamSineWave(client);
//     } else {
//       client.println("ERROR: Unknown command.");
//     }

//     client.stop();
//     Serial.println("Client disconnected.\n");
//   }
// }