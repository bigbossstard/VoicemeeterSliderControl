const int NUM_SLIDERS = 8;
const int analogInputs[NUM_SLIDERS] = {A0, A1, A2, A3, A6, A7, A8, A9};
float currentValues[NUM_SLIDERS];
unsigned long lastSend = 0;
const unsigned long SEND_INTERVAL = 20; // мс — 50 обновлений/сек, глазу достаточно

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < NUM_SLIDERS; i++) {
    currentValues[i] = analogRead(analogInputs[i]);
  }
}

void loop() {
  for (int i = 0; i < NUM_SLIDERS; i++) {
    int raw = analogRead(analogInputs[i]);
    currentValues[i] = currentValues[i] * 0.7 + raw * 0.3;
  }

  if (millis() - lastSend >= SEND_INTERVAL) {
    lastSend = millis();
    for (int i = 0; i < NUM_SLIDERS; i++) {
      Serial.print((int)currentValues[i]);
      if (i < NUM_SLIDERS - 1) Serial.print("|");
    }
    Serial.println();
  }
}