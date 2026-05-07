using OxyPlot;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ESP32StreamManager
{
    public partial class MainForm
    {
        private void OpenMobileHotspotSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network-mobilehotspot",
                    UseShellExecute = true
                });

                Log("Открыты настройки мобильного хот-спота Windows", "INFO");
            }
            catch (Exception ex)
            {
                Log($"Не удалось открыть настройки хот-спота: {ex.Message}", "ERROR");
            }
        }

        private void OpenConfigEditor()
        {
            using var form = new ConfigEditorForm(_config);

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _config = form.Config;
                SaveConfig();
                UpdateUI();

                Log("Настройки конфигурации обновлены", "SUCCESS");
            }
        }

        private void ConfigureSingleEsp()
        {
            var device = GetMainDevice();

            if (device == null)
            {
                MessageBox.Show("Устройство не задано в конфигурации.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(
                $"Убедитесь, что {device.Name} включен.\n\n" +
                $"Для настройки необходимо подключиться к Wi-Fi сети устройства:\n" +
                $"SSID: {device.ApSsid}\n" +
                $"Пароль: {device.ApPassword}",
                "Подготовка к настройке",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Task.Run(() => ConfigureEspDevice(device));
        }

        private void FindEspInHotspotNetwork()
        {
            var device = GetMainDevice();

            if (device == null)
            {
                MessageBox.Show("Устройство не задано в конфигурации.", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log("Поиск ESP32 в сети...", "INFO", device.Name);

            Task.Run(() =>
            {
                bool found = FindAndSaveEspInNetwork(device);

                if (found)
                {
                    SaveConfig();
                    Log("Устройство найдено", "SUCCESS", device.Name);
                }
                else
                {
                    Log("Устройство не найдено", "ERROR", device.Name);
                }

                UpdateUI();
            });
        }

        private void TogglePrediction()
        {
            if (_segmenter == null)
            {
                MessageBox.Show(
                    "Модель не загружена. Проверьте файл ML/best_model.onnx.",
                    "Предсказания недоступны",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            _predictionEnabled = !_predictionEnabled;

            btnTogglePrediction.Text = _predictionEnabled
                ? "🧠  Предсказания: ВКЛ"
                : "🧠  Предсказания: ВЫКЛ";

            Log(
                _predictionEnabled
                    ? "Нейросетевые предсказания включены"
                    : "Нейросетевые предсказания выключены",
                "INFO");
        }

        private void SaveRecordingToCsv()
        {
            try
            {
                List<DataPoint> snapshot;

                lock (ecgData)
                {
                    if (ecgData.Count == 0)
                    {
                        MessageBox.Show(
                            "Нет данных для сохранения.",
                            "Сохранение записи",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        return;
                    }

                    snapshot = ecgData.ToList();
                }

                using var dialog = new SaveFileDialog
                {
                    Title = "Сохранить запись ЭКГ",
                    Filter = "CSV файл (*.csv)|*.csv",
                    FileName = $"ecg_record_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                var sb = new StringBuilder();

                sb.AppendLine("time_sec,adc_value");

                foreach (var point in snapshot)
                {
                    sb.AppendLine(
                        $"{point.X.ToString(CultureInfo.InvariantCulture)}," +
                        $"{point.Y.ToString(CultureInfo.InvariantCulture)}");
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                Log($"Запись сохранена: {dialog.FileName}", "SUCCESS");

                MessageBox.Show(
                    "Запись успешно сохранена.",
                    "Сохранение записи",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"Ошибка сохранения записи: {ex.Message}", "ERROR");

                MessageBox.Show(
                    $"Не удалось сохранить запись:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }


}