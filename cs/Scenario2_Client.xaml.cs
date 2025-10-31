using System;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace SDKTemplate
{
    public sealed partial class Scenario2_Client : Page
    {
        // UUIDs de las características (según tu sketch ESP32)
        private static readonly Guid SERVICE_UUID   = ParseGuidSafe("12345678-1234-1234-1234-1234567890ab");
        private static readonly Guid SPEED_UUID     = ParseGuidSafe("12345678-1234-1234-1234-1234567890ac");
        private static readonly Guid TEMP_UUID      = ParseGuidSafe("12345678-1234-1234-1234-1234567890ad");
        private static readonly Guid RUNTIME_UUID   = ParseGuidSafe("12345678-1234-1234-1234-1234567890ae");

        private BluetoothLEDevice connectedDevice;
        private GattDeviceService connectedService;

        private GattCharacteristic speedChar;
        private GattCharacteristic tempChar;
        private GattCharacteristic runtimeChar;

        public Scenario2_Client()
        {
            this.InitializeComponent();
            UpdateUiDisconnected();
        }

        // Método público para iniciar la conexión desde tu flujo (p. ej. pasando bluetooth address)
        public async Task<bool> ConnectToDeviceAsync(ulong bluetoothAddress)
        {
            try
            {
                Log($"ConnectToDeviceAsync: address=0x{bluetoothAddress:X}");
                UpdateStatus("Connecting...");
                connectedDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);

                if (connectedDevice == null)
                {
                    UpdateStatus("Failed to connect to device.");
                    Log("BluetoothLEDevice.FromBluetoothAddressAsync returned null.");
                    return false;
                }

                Log($"ConnectedDevice: Name='{connectedDevice?.Name}', Id='{connectedDevice?.DeviceId}'");
                SelectedDeviceName.Text = connectedDevice.Name ?? connectedDevice.DeviceId;

                var result = await connectedDevice.GetGattServicesForUuidAsync(SERVICE_UUID, BluetoothCacheMode.Uncached);
                Log($"GetGattServicesForUuidAsync status={{result.Status}}, servicesCount={{result.Services?.Count ?? 0}});

                if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
                {
                    UpdateStatus("Service not found on device.");
                    Log("Service not found - check SERVICE_UUID and that ESP32 is advertising the service.");
                    await CleanUpConnectionAsync();
                    return false;
                }

                connectedService = result.Services[0];

                // Suscribirse automáticamente a las características de interés
                await SubscribeToCharacteristics(connectedService);

                UpdateUiConnected();
                UpdateStatus("Connected and subscribed.");
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Connect error: {ex.Message}");
                Log($"Connect exception: {ex}");
                await CleanUpConnectionAsync();
                return false;
            }
        }

        private async Task SubscribeToCharacteristics(GattDeviceService service)
        {
            // Limpia posibles handlers previos
            DetachCharacteristicHandlers();

            var charsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            Log($"GetCharacteristicsAsync status={{charsResult.Status}}, count={{charsResult.Characteristics?.Count ?? 0}});
            if (charsResult.Status != GattCommunicationStatus.Success)
            {
                UpdateStatus("Failed to get characteristics.");
                Log("Failed to get characteristics from service.");
                return;
            }

            foreach (var c in charsResult.Characteristics)
            {
                try
                {
                    if (c.Uuid == SPEED_UUID)
                    {
                        speedChar = c;
                        speedChar.ValueChanged += SpeedChar_ValueChanged;
                        await EnableNotifyAsync(speedChar);
                        Log($"Subscribed to SPEED ({{c.Uuid}});");
                    }
                    else if (c.Uuid == TEMP_UUID)
                    {
                        tempChar = c;
                        tempChar.ValueChanged += TempChar_ValueChanged;
                        await EnableNotifyAsync(tempChar);
                        Log($"Subscribed to TEMP ({{c.Uuid}});");
                    }
                    else if (c.Uuid == RUNTIME_UUID)
                    {
                        runtimeChar = c;
                        runtimeChar.ValueChanged += RuntimeChar_ValueChanged;
                        await EnableNotifyAsync(runtimeChar);
                        Log($"Subscribed to RUNTIME ({{c.Uuid}});");
                    }
                    else
                    {
                        Log($"Skipping characteristic {{c.Uuid}});");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Subscribe error for {{c.Uuid}}: {{ex.Message}});");
                }
            }

            // Actualizar botones de UI según suscripciones
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool hasAny = speedChar != null || tempChar != null || runtimeChar != null;
                UnsubscribeButton.IsEnabled = hasAny;
                ReadValueButton.IsEnabled = hasAny;
                DisconnectButton.IsEnabled = true;

                // MODO DEPURACIÓN: habilita lectura/desconexión mientras depuras si quieres.
                // ReadValueButton.IsEnabled = true;
                // DisconnectButton.IsEnabled = true;
            });
        }

        private async Task EnableNotifyAsync(GattCharacteristic characteristic)
        {
            if (characteristic == null) return;

            var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
            if (status == GattCommunicationStatus.Success)
            {
                Log($"Enabled Notify for {{characteristic.Uuid}});");
            }
            else
            {
                Log($"Failed to enable notify for {{characteristic.Uuid}}: {{status}});");
            }
        }

        // Lectura manual (lee la primera característica disponible entre las 3)
        private async void ReadValueButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GattCharacteristic c = speedChar ?? tempChar ?? runtimeChar;
                if (c == null)
                {
                    Log("No characteristic available to read.");
                    return;
                }
                var read = await c.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (read.Status == GattCommunicationStatus.Success)
                {
                    string value = ReadStringFromBuffer(read.Value);
                    Log($"Read {{c.Uuid}}: {{value}});");
                }
                else
                {
                    Log($"Read failed: {{read.Status}});");
                }
            }
            catch (Exception ex)
            {
                Log($"Read exception: {{ex.Message}});");
            }
        }

        // Unsubscribe manual
        private async void UnsubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            await UnsubscribeAllAsync();
        }

        private async Task UnsubscribeAllAsync()
        {
            try
            {
                if (speedChar != null)
                {
                    speedChar.ValueChanged -= SpeedChar_ValueChanged;
                    await speedChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    speedChar = null;
                }
                if (tempChar != null)
                {
                    tempChar.ValueChanged -= TempChar_ValueChanged;
                    await tempChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    tempChar = null;
                }
                if (runtimeChar != null)
                {
                    runtimeChar.ValueChanged -= RuntimeChar_ValueChanged;
                    await runtimeChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    runtimeChar = null;
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    SpeedText.Text = "Velocidad: -";
                    TempText.Text = "Temperatura: -";
                    RuntimeText.Text = "Tiempo de funcionamiento: -";
                    UnsubscribeButton.IsEnabled = false;
                    ReadValueButton.IsEnabled = false;
                });

                Log("Unsubscribed from all characteristics.");
            }
            catch (Exception ex)
            {
                Log($"Unsubscribe error: {{ex.Message}});");
            }
        }

        // Desconectar (botón)
        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await CleanUpConnectionAsync();
            UpdateUiDisconnected();
        }

        // Limpiar y liberar recursos GATT
        private async Task CleanUpConnectionAsync()
        {
            try
            {
                await UnsubscribeAllAsync();

                if (connectedService != null)
                {
                    connectedService.Dispose();
                    connectedService = null;
                }
                if (connectedDevice != null)
                {
                    connectedDevice.Dispose();
                    connectedDevice = null;
                }

                Log("Connection cleaned up.");
            }
            catch (Exception ex)
            {
                Log($"Cleanup error: {{ex.Message}});");
            }
        }

        private void DetachCharacteristicHandlers()
        {
            try
            {
                if (speedChar != null)
                {
                    speedChar.ValueChanged -= SpeedChar_ValueChanged;
                    speedChar = null;
                }
                if (tempChar != null)
                {
                    tempChar.ValueChanged -= TempChar_ValueChanged;
                    tempChar = null;
                }
                if (runtimeChar != null)
                {
                    runtimeChar.ValueChanged -= RuntimeChar_ValueChanged;
                    runtimeChar = null;
                }
            }
            catch { /* swallow */ }
        }

        // Handlers de eventos ValueChanged
        private async void SpeedChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = ReadStringFromBuffer(args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SpeedText.Text = $"Velocidad: {{value}} RPM";
            });
        }

        private async void TempChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = ReadStringFromBuffer(args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TempText.Text = $"Temperatura: {{value}} °C";
            });
        }

        private async void RuntimeChar_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = ReadStringFromBuffer(args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                RuntimeText.Text = $"Tiempo de funcionamiento: {{value}} s";
            });
        }

        // Util: leer buffer como string (tu firmware envía ASCII decimal)
        private string ReadStringFromBuffer(IBuffer buffer)
        {
            try
            {
                var reader = DataReader.FromBuffer(buffer);
                uint len = buffer.Length;
                string s = reader.ReadString(len);
                return s.Trim();
            }
            catch
            {
                // Si no es string, intenta interpretar como números
                try
                {
                    var data = buffer.ToArray();
                    return BitConverter.ToString(data);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        // Util: log y status
        private void Log(string message)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LogText.Text = $"{{DateTime.Now:HH:mm:ss}}: {{message}}\n{{LogText.Text}}";
            });
        }

        private void UpdateStatus(string s)
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                StatusText.Text = $"Status: {{s}}";
                LogText.Text = $"{{DateTime.Now:HH:mm:ss}}: {{s}}\n{{LogText.Text}}";
            });
        }

        private void UpdateUiConnected()
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                DisconnectButton.IsEnabled = true;
                bool hasAny = speedChar != null || tempChar != null || runtimeChar != null;
                ReadValueButton.IsEnabled = hasAny;
                UnsubscribeButton.IsEnabled = hasAny;
                // MODO DEPURACIÓN: descomenta para forzar habilitación mientras depuras
                // ReadValueButton.IsEnabled = true;
            });
        }

        private void UpdateUiDisconnected()
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                DisconnectButton.IsEnabled = false;
                ReadValueButton.IsEnabled = false;
                UnsubscribeButton.IsEnabled = false;
                SelectedDeviceName.Text = "-";
            });
        }

        // Helper para parse seguro de GUID (detecta problemas de cadena)
        private static Guid ParseGuidSafe(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException("GUID string is null or empty.", nameof(s));

            s = s.Trim().Trim('"');

            if (Guid.TryParse(s, out Guid g))
            {
                return g;
            }
            else
            {
                throw new FormatException($"GUID inválido: '{{s}}'. Un GUID debe tener 32 dígitos hex con 4 guiones: 8-4-4-4-12.");
            }
        }
    }

    // Extension method helper for IBuffer to copy contents (if needed)
    static class BufferExtensions
    {
        public static byte[] ToArray(this Windows.Storage.Streams.IBuffer buffer)
        {
            var data = new byte[buffer.Length];
            DataReader.FromBuffer(buffer).ReadBytes(data);
            return data;
        }
    }
}