#include <Arduino.h>

void setup() {
  // Инициализируем последовательный порт
  Serial.begin(115200);
  
  // Ждем немного для стабилизации
  delay(1000);
  
  // Включаем встроенный светодиод (на GPIO2)
  pinMode(2, OUTPUT);
  
  Serial.println("===================================");
  Serial.println("ESP32 ТЕСТОВАЯ ПРОГРАММА");
  Serial.println("===================================");
  Serial.println("Если вы видите этот текст,");
  Serial.println("ESP32 работает корректно!");
  Serial.println("===================================");
}

void loop() {
  static int counter = 0;
  
  // Выводим счетчик
  Serial.print("Счетчик: ");
  Serial.println(counter);
  
  // Мигаем светодиодом
  digitalWrite(2, HIGH);
  Serial.println("Светодиод ВКЛ");
  delay(500);
  
  digitalWrite(2, LOW);
  Serial.println("Светодиод ВЫКЛ");
  delay(500);
  
  // Увеличиваем счетчик
  counter++;
  
  // Каждые 10 итераций выводим разделитель
  if (counter % 10 == 0) {
    Serial.println("-----------------------------------");
    Serial.print("Прошло циклов: ");
    Serial.println(counter);
    Serial.println("-----------------------------------");
  }
}