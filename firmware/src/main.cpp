// #include <WiFi.h>
// #include <WiFiClient.h>
// #include <WiFiAP.h>
// #include <Preferences.h>

// Preferences prefs;
// WiFiServer server(8888);
// bool apStarted = false;
// bool serverStarted = false;

// void setup() {
//   Serial.begin(115200);
//   delay(2000); // Важно дать время для стабилизации
  
//   Serial.println("\n=== ESP32 Simple Server ===");
  
//   // Инициализация памяти
//   prefs.begin("wifi", false);
  
//   // Всегда запускаем AP
//   Serial.println("Starting Access Point...");
//   apStarted = WiFi.softAP("ESP32_AP", "12345678");
  
//   if (apStarted) {
//     Serial.print("AP IP: ");
//     Serial.println(WiFi.softAPIP());
//   } else {
//     Serial.println("Failed to start AP!");
//     return;
//   }
  
//   // Запускаем сервер
//   server.begin();
//   serverStarted = true;
//   Serial.println("TCP server started on port 8888");
  
//   // Проверяем сохраненные сети WiFi
//   String ssid = prefs.getString("ssid", "");
//   String pass = prefs.getString("pass", "");
  
//   if (ssid.length() > 0) {
//     Serial.println("Found saved WiFi, connecting...");
//     WiFi.begin(ssid.c_str(), pass.c_str());
//   }
// }

// void loop() {
//   // Проверяем клиентов
//   WiFiClient client = server.available();
  
//   if (client) {
//     Serial.println("New client connected");
//     client.setTimeout(5000);
    
//     // Читаем команду
//     if (client.available()) {
//       String command = client.readStringUntil('\n');
//       command.trim();
//       Serial.println("Command: " + command);
      
//       if (command == "PING") {
//         client.println("PONG");
//       }
//       else if (command == "SET") {
//         // Читаем SSID и пароль
//         String ssid = client.readStringUntil('\n');
//         String pass = client.readStringUntil('\n');
//         ssid.trim();
//         pass.trim();
        
//         if (ssid.length() > 0 && pass.length() >= 8) {
//           prefs.putString("ssid", ssid);
//           prefs.putString("pass", pass);
//           client.println("OK: Credentials saved");
//           Serial.println("Credentials saved");
//         } else {
//           client.println("ERROR: Invalid credentials");
//         }
//       }
//       else if (command == "STATUS") {
//         client.println("AP: ESP32_AP");
//         client.print("AP IP: "); client.println(WiFi.softAPIP());
//         client.print("WiFi: "); 
//         if (WiFi.status() == WL_CONNECTED) {
//           client.println(WiFi.SSID());
//           client.print("IP: "); client.println(WiFi.localIP());
//         } else {
//           client.println("Not connected");
//         }
//         client.println("END");
//       }
//       else {
//         client.println("ERROR: Unknown command");
//       }
//     }
    
//     client.stop();
//     Serial.println("Client disconnected");
//   }
  
//   // Медленный loop для стабильности
//   delay(100);
  
//   // Периодический статус
//   static unsigned long lastStatus = 0;
//   if (millis() - lastStatus > 30000) {
//     lastStatus = millis();
//     Serial.printf("Status: AP=%s, Server=%s, Heap=%d\n",
//                   apStarted ? "OK" : "FAIL",
//                   serverStarted ? "OK" : "FAIL",
//                   ESP.getFreeHeap());
//   }
// }