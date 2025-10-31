//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SDKTemplate
{
    // This scenario connects to the device selected in the "Discover
    // GATT Servers" scenario and communicates with it.
    // Note that this scenario is rather artificial because it communicates
    // with an unknown service with unknown characteristics.
    // In practice, your app will be interested in a specific service with
    // a specific characteristic.
    public sealed partial class Scenario2_Client : Page
    {
        private MainPage rootPage = MainPage.Current;

        private BluetoothLEDevice bluetoothLeDevice = null;
        private GattCharacteristic selectedCharacteristic = null;

        // Only one registered characteristic at a time.
        private GattCharacteristic registeredCharacteristic = null;

        // MotorMonitor characteristics
        private GattCharacteristic speedCharacteristic = null;
        private GattCharacteristic tempCharacteristic = null;
        private GattCharacteristic runtimeCharacteristic = null;

        #region UI Code
        public Scenario2_Client()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            SelectedDeviceRun.Text = rootPage.SelectedBleDeviceName;
            if (string.IsNullOrEmpty(rootPage.SelectedBleDeviceId))
            {
                ConnectButton.IsEnabled = false;
            }
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            await ClearBluetoothLEDeviceAsync();
        }

        /// <summary>
        /// Safely parse a GUID string, returning null if parsing fails
        /// </summary>
        private Guid? ParseGuidSafe(string guidString)
        {
            if (Guid.TryParse(guidString, out Guid result))
            {
                return result;
            }
            return null;
        }

        /// <summary>
        /// Log a message to the LogText UI element
        /// </summary>
        private async void Log(string message)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                LogText.Text += $"[{timestamp}] {message}\n";
                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
            });
        }

        /// <summary>
        /// Update status in both the log and the root page
        /// </summary>
        private void UpdateStatus(string message, NotifyType type = NotifyType.StatusMessage)
        {
            Log(message);
            rootPage.NotifyUser(message, type);
        }
        #endregion

        #region Enumerating Services
        private async Task ClearBluetoothLEDeviceAsync()
        {
            await UnsubscribeAllAsync();
            await CleanUpConnectionAsync();
        }

        /// <summary>
        /// Unsubscribe from all MotorMonitor characteristics
        /// </summary>
        private async Task UnsubscribeAllAsync()
        {
            // Unsubscribe from MotorMonitor characteristics
            if (speedCharacteristic != null)
            {
                speedCharacteristic.ValueChanged -= SpeedCharacteristic_ValueChanged;
                await WriteClientCharacteristicConfigurationDescriptorSafe(speedCharacteristic, GattClientCharacteristicConfigurationDescriptorValue.None);
                speedCharacteristic = null;
            }

            if (tempCharacteristic != null)
            {
                tempCharacteristic.ValueChanged -= TempCharacteristic_ValueChanged;
                await WriteClientCharacteristicConfigurationDescriptorSafe(tempCharacteristic, GattClientCharacteristicConfigurationDescriptorValue.None);
                tempCharacteristic = null;
            }

            if (runtimeCharacteristic != null)
            {
                runtimeCharacteristic.ValueChanged -= RuntimeCharacteristic_ValueChanged;
                await WriteClientCharacteristicConfigurationDescriptorSafe(runtimeCharacteristic, GattClientCharacteristicConfigurationDescriptorValue.None);
                runtimeCharacteristic = null;
            }

            // Unsubscribe from old registered characteristic
            GattCharacteristic characteristic = registeredCharacteristic;
            registeredCharacteristic = null;

            if (characteristic != null)
            {
                characteristic.ValueChanged -= Characteristic_ValueChanged;
                await WriteClientCharacteristicConfigurationDescriptorSafe(characteristic, GattClientCharacteristicConfigurationDescriptorValue.None);
            }

            // Reset UI
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SpeedText.Text = "SPEED: -";
                TempText.Text = "TEMP: -";
                RuntimeText.Text = "RUNTIME: -";
            });
        }

        /// <summary>
        /// Safely write CCCD without throwing exceptions
        /// </summary>
        private async Task WriteClientCharacteristicConfigurationDescriptorSafe(GattCharacteristic characteristic, GattClientCharacteristicConfigurationDescriptorValue value)
        {
            try
            {
                GattCommunicationStatus result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(value);
                if (result != GattCommunicationStatus.Success)
                {
                    Log($"Warning: Unable to write CCCD for characteristic {characteristic.Uuid}");
                }
            }
            catch (Exception ex)
            {
                Log($"Warning: Exception writing CCCD: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up the BLE connection and dispose resources
        /// </summary>
        private async Task CleanUpConnectionAsync()
        {
            if (bluetoothLeDevice != null)
            {
                bluetoothLeDevice.Dispose();
                bluetoothLeDevice = null;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                MonitorPanel.Visibility = Visibility.Collapsed;
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
                ConnectButton.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Connect to the MotorMonitor device and automatically subscribe to its characteristics
        /// </summary>
        public async Task ConnectToDeviceAsync(string deviceId)
        {
            await ClearBluetoothLEDeviceAsync();

            // Show log
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                LogHeader.Visibility = Visibility.Visible;
                LogScrollViewer.Visibility = Visibility.Visible;
                LogText.Text = "";
            });

            UpdateStatus("Connecting to device...");

            // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
            bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(deviceId);

            if (bluetoothLeDevice == null)
            {
                UpdateStatus("Unable to find device. Maybe it isn't connected any more.", NotifyType.ErrorMessage);
                return;
            }

            UpdateStatus($"Connected to {bluetoothLeDevice.Name}");

            // Discover the MotorMonitor service by UUID
            Guid? serviceUuid = ParseGuidSafe(Constants.MotorMonitorServiceUuid.ToString());
            if (serviceUuid == null)
            {
                UpdateStatus("Error: Invalid service UUID", NotifyType.ErrorMessage);
                return;
            }

            UpdateStatus($"Discovering service {serviceUuid}...");
            GattDeviceServicesResult servicesResult = await bluetoothLeDevice.GetGattServicesForUuidAsync(serviceUuid.Value, BluetoothCacheMode.Uncached);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                UpdateStatus($"Error accessing services: {Utilities.FormatGattCommunicationStatus(servicesResult.Status, servicesResult.ProtocolError)}", NotifyType.ErrorMessage);
                return;
            }

            if (servicesResult.Services.Count == 0)
            {
                UpdateStatus("MotorMonitor service not found. Falling back to manual mode.", NotifyType.ErrorMessage);
                // Fall back to the original manual service selection
                await ConnectManualAsync();
                return;
            }

            GattDeviceService motorService = servicesResult.Services[0];
            UpdateStatus($"Found service: {motorService.Uuid}");

            // Discover and subscribe to SPEED, TEMP, and RUNTIME characteristics
            await DiscoverAndSubscribeCharacteristicsAsync(motorService);
        }

        /// <summary>
        /// Discover and subscribe to all three MotorMonitor characteristics
        /// </summary>
        private async Task DiscoverAndSubscribeCharacteristicsAsync(GattDeviceService service)
        {
            // Request access to the service
            DeviceAccessStatus accessStatus = await service.RequestAccessAsync();
            if (accessStatus != DeviceAccessStatus.Allowed)
            {
                UpdateStatus("Error: Access to service denied.", NotifyType.ErrorMessage);
                return;
            }

            // Get all characteristics
            GattCharacteristicsResult characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (characteristicsResult.Status != GattCommunicationStatus.Success)
            {
                UpdateStatus($"Error accessing characteristics: {Utilities.FormatGattCommunicationStatus(characteristicsResult.Status, characteristicsResult.ProtocolError)}", NotifyType.ErrorMessage);
                return;
            }

            UpdateStatus($"Found {characteristicsResult.Characteristics.Count} characteristic(s)");

            // Find and subscribe to each characteristic
            foreach (var characteristic in characteristicsResult.Characteristics)
            {
                if (characteristic.Uuid == Constants.SpeedCharacteristicUuid)
                {
                    speedCharacteristic = characteristic;
                    await EnableNotifyAsync(speedCharacteristic, "SPEED", SpeedCharacteristic_ValueChanged);
                }
                else if (characteristic.Uuid == Constants.TempCharacteristicUuid)
                {
                    tempCharacteristic = characteristic;
                    await EnableNotifyAsync(tempCharacteristic, "TEMP", TempCharacteristic_ValueChanged);
                }
                else if (characteristic.Uuid == Constants.RuntimeCharacteristicUuid)
                {
                    runtimeCharacteristic = characteristic;
                    await EnableNotifyAsync(runtimeCharacteristic, "RUNTIME", RuntimeCharacteristic_ValueChanged);
                }
            }

            // Show the monitoring UI
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                MonitorPanel.Visibility = Visibility.Visible;
                ActionButtonsPanel.Visibility = Visibility.Visible;
                ConnectButton.Visibility = Visibility.Collapsed;
            });

            UpdateStatus("Connected and subscribed.");
        }

        /// <summary>
        /// Enable notifications for a characteristic and attach a value changed handler
        /// </summary>
        private async Task EnableNotifyAsync(GattCharacteristic characteristic, string name, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler)
        {
            if (characteristic == null)
            {
                UpdateStatus($"Error: {name} characteristic is null", NotifyType.ErrorMessage);
                return;
            }

            // Attach the value changed handler
            characteristic.ValueChanged += handler;

            // Enable notifications
            try
            {
                GattWriteResult result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    UpdateStatus($"Subscribed to {name}");
                }
                else
                {
                    UpdateStatus($"Failed to subscribe to {name}: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Exception subscribing to {name}: {ex.Message}", NotifyType.ErrorMessage);
            }
        }

        /// <summary>
        /// Fallback to manual service/characteristic selection
        /// </summary>
        private async Task ConnectManualAsync()
        {
            try
            {
                GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    IReadOnlyList<GattDeviceService> services = result.Services;
                    UpdateStatus($"Found {services.Count} services");

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        foreach (var service in services)
                        {
                            ServiceList.Items.Add(new ComboBoxItem { Content = DisplayHelpers.GetServiceName(service), Tag = service });
                        }
                        ServiceList.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    UpdateStatus($"Error: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", NotifyType.ErrorMessage);
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectButton.IsEnabled = false;

            await ConnectToDeviceAsync(rootPage.SelectedBleDeviceId);

            ConnectButton.IsEnabled = true;
        }
        #endregion

        #region Enumerating Characteristics
        private async void ServiceList_SelectionChanged(object sender, RoutedEventArgs e)
        {
            CharacteristicList.Items.Clear();
            CharacteristicList.Visibility = Visibility.Collapsed;
            RemoveValueChangedHandler();

            var service = (GattDeviceService)((ComboBoxItem)ServiceList.SelectedItem)?.Tag;
            if (service == null)
            {
                rootPage.NotifyUser("No service selected", NotifyType.ErrorMessage);
                return;
            }

            IReadOnlyList<GattCharacteristic> characteristics = null;
            try
            {
                // Ensure we have access to the device.
                DeviceAccessStatus accessStatus = await service.RequestAccessAsync();
                if (accessStatus != DeviceAccessStatus.Allowed)
                {
                    // Not granted access
                    rootPage.NotifyUser("Error accessing service.", NotifyType.ErrorMessage);
                    return;
                }

                // BT_Code: Get all the child characteristics of a service. Use the cache mode to specify uncached characteristics only
                // and the new Async functions to get the characteristics of unpaired devices as well.
                GattCharacteristicsResult result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (result.Status != GattCommunicationStatus.Success)
                {
                    rootPage.NotifyUser($"Error accessing service: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                    return;
                }
                characteristics = result.Characteristics;
            }
            catch (Exception ex)
            {
                // The service might not be running, or it failed to provide characteristics.
                rootPage.NotifyUser("Restricted service. Can't read characteristics: " + ex.Message, NotifyType.ErrorMessage);
                return;
            }

            foreach (GattCharacteristic c in characteristics)
            {
                CharacteristicList.Items.Add(new ComboBoxItem { Content = DisplayHelpers.GetCharacteristicName(c), Tag = c });
            }
            CharacteristicList.Visibility = Visibility.Visible;
        }
        #endregion

        private void AddValueChangedHandler()
        {
            ValueChangedSubscribeToggle.Content = "Unsubscribe from value changes";
            if (registeredCharacteristic == null)
            {
                registeredCharacteristic = selectedCharacteristic;
                registeredCharacteristic.ValueChanged += Characteristic_ValueChanged;
            }
        }

        private void RemoveValueChangedHandler()
        {
            ValueChangedSubscribeToggle.Content = "Subscribe to value changes";
            if (registeredCharacteristic != null)
            {
                registeredCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                registeredCharacteristic = null;
            }
        }

        private async void CharacteristicList_SelectionChanged(object sender, RoutedEventArgs e)
        {
            selectedCharacteristic = (GattCharacteristic)((ComboBoxItem)CharacteristicList.SelectedItem)?.Tag;
            if (selectedCharacteristic == null)
            {
                EnableCharacteristicPanels(GattCharacteristicProperties.None);
                rootPage.NotifyUser("No characteristic selected", NotifyType.ErrorMessage);
                return;
            }

            // Get all the child descriptors of a characteristics. Use the cache mode to specify uncached descriptors only
            // and the new Async functions to get the descriptors of unpaired devices as well.
            GattDescriptorsResult result = await selectedCharacteristic.GetDescriptorsAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
            {
                rootPage.NotifyUser($"Descriptor read failure: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
            }

            // Enable/disable operations based on the GattCharacteristicProperties.
            EnableCharacteristicPanels(selectedCharacteristic.CharacteristicProperties);
        }

        private void SetVisibility(UIElement element, bool visible)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EnableCharacteristicPanels(GattCharacteristicProperties properties)
        {
            // BT_Code: Hide the controls which do not apply to this characteristic.
            SetVisibility(CharacteristicReadButton, properties.HasFlag(GattCharacteristicProperties.Read));

            SetVisibility(CharacteristicWritePanel,
                properties.HasFlag(GattCharacteristicProperties.Write) ||
                properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse));
            CharacteristicWriteValue.Text = "";

            SetVisibility(ValueChangedSubscribeToggle, properties.HasFlag(GattCharacteristicProperties.Indicate) ||
                                                       properties.HasFlag(GattCharacteristicProperties.Notify));
            ValueChangedSubscribeToggle.IsEnabled =
                (registeredCharacteristic == null) ||
                (registeredCharacteristic == selectedCharacteristic);
        }

        private async void CharacteristicReadButton_Click(object sender, RoutedEventArgs e)
        {
            // Capture the characteristic we are reading from, in case the use changes the selection during the await.
            GattCharacteristic characteristic = selectedCharacteristic;

            // BT_Code: Read the actual value from the device by using Uncached.
            try
            {
                GattReadResult result = await selectedCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    string formattedResult = FormatValueByPresentation(characteristic, result.Value);
                    rootPage.NotifyUser($"Read result: {formattedResult}", NotifyType.StatusMessage);
                }
                else
                {
                    // This can happen when a device reports that it support reading, but it actually doesn't.
                    rootPage.NotifyUser($"Read failed: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                }
            }
            catch (ObjectDisposedException)
            {
                // Server is no longer available.
                rootPage.NotifyUser("Read failed: Service is no longer available.", NotifyType.ErrorMessage);
            }
        }

        private async void CharacteristicWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!String.IsNullOrEmpty(CharacteristicWriteValue.Text))
            {
                var writeBuffer = CryptographicBuffer.ConvertStringToBinary(CharacteristicWriteValue.Text,
                    BinaryStringEncoding.Utf8);

                // WriteBufferToSelectedCharacteristicAsync will display an error message on failure
                // so we don't have to.
                await WriteBufferToSelectedCharacteristicAsync(writeBuffer);
            }
            else
            {
                rootPage.NotifyUser("No data to write to device", NotifyType.ErrorMessage);
            }
        }

        private async void CharacteristicWriteButtonInt_Click(object sender, RoutedEventArgs e)
        {
            if (Int32.TryParse(CharacteristicWriteValue.Text, out int writeValue))
            {
                // WriteBufferToSelectedCharacteristicAsync will display an error message on failure
                // so we don't have to.
                await WriteBufferToSelectedCharacteristicAsync(BufferHelpers.BufferFromInt32(writeValue));
            }
            else
            {
                rootPage.NotifyUser("Data to write has to be an int32", NotifyType.ErrorMessage);
            }
        }

        private async Task<bool> WriteBufferToSelectedCharacteristicAsync(IBuffer buffer)
        {
            // BT_Code: Writes the value from the buffer to the characteristic.
            try
            {
                GattWriteResult result = await selectedCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    rootPage.NotifyUser("Successfully wrote value to device", NotifyType.StatusMessage);
                    return true;
                }
                else
                {
                    // This can happen, for example, if a device reports that it supports writing, but it actually doesn't.
                    rootPage.NotifyUser($"Write failed: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                // Server is no longer available.
                rootPage.NotifyUser("Write failed: Service is no longer available.", NotifyType.ErrorMessage);
                return false;
            }
        }

        private async void ValueChangedSubscribeToggle_Click(object sender, RoutedEventArgs e)
        {
            string operation = null;
            // initialize status
            var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
            if (registeredCharacteristic != null)
            {
                // Unsubscribe by specifying "None"
                operation = "Unsubscribe";
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
            }
            else if (selectedCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                // Subscribe with "indicate"
                operation = "Subscribe";
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }
            else if (selectedCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                // Subscribe with "notify"
                operation = "Subscribe";
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }
            else
            {
                // Unreachable because the button is disabled if it cannot indicate or notify.
            }

            // BT_Code: Must write the CCCD in order for server to send indications.
            // We receive them in the ValueChanged event handler.
            try
            {
                GattWriteResult result = await selectedCharacteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(cccdValue);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    rootPage.NotifyUser($"{operation} succeeded", NotifyType.StatusMessage);
                    if (cccdValue != GattClientCharacteristicConfigurationDescriptorValue.None)
                    {
                        AddValueChangedHandler();
                    }
                    else
                    {
                        RemoveValueChangedHandler();
                    }
                }
                else
                {
                    // This can happen when a device reports that it supports indicate, but it actually doesn't.
                    rootPage.NotifyUser($"{operation} failed: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                }
            }
            catch (ObjectDisposedException)
            {
                // Service is no longer available.
                rootPage.NotifyUser($"{operation} failed: Service is no longer available.", NotifyType.ErrorMessage);
            }
        }

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            // Read all three characteristics
            if (speedCharacteristic != null)
            {
                await ReadCharacteristicValueAsync(speedCharacteristic, "SPEED");
            }
            if (tempCharacteristic != null)
            {
                await ReadCharacteristicValueAsync(tempCharacteristic, "TEMP");
            }
            if (runtimeCharacteristic != null)
            {
                await ReadCharacteristicValueAsync(runtimeCharacteristic, "RUNTIME");
            }
        }

        private async Task ReadCharacteristicValueAsync(GattCharacteristic characteristic, string name)
        {
            try
            {
                GattReadResult result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    string value = FormatValueByPresentation(characteristic, result.Value);
                    UpdateStatus($"{name} read: {value}");
                }
                else
                {
                    UpdateStatus($"Failed to read {name}: {Utilities.FormatGattCommunicationStatus(result.Status, result.ProtocolError)}", NotifyType.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Exception reading {name}: {ex.Message}", NotifyType.ErrorMessage);
            }
        }

        private async void UnsubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            await UnsubscribeAllAsync();
            UpdateStatus("Unsubscribed from all characteristics");
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await ClearBluetoothLEDeviceAsync();
            UpdateStatus("Disconnected");
        }

        private async void SpeedCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = FormatValueByPresentation(sender, args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SpeedText.Text = $"SPEED: {value}";
            });
        }

        private async void TempCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = FormatValueByPresentation(sender, args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TempText.Text = $"TEMP: {value}";
            });
        }

        private async void RuntimeCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            string value = FormatValueByPresentation(sender, args.CharacteristicValue);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                RuntimeText.Text = $"RUNTIME: {value}";
            });
        }

        private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // BT_Code: An Indicate or Notify reported that the value has changed.
            // Display the new value with a timestamp.
            string newValue = FormatValueByPresentation(sender, args.CharacteristicValue);
            string message = $"Value of \"{DisplayHelpers.GetCharacteristicName(sender)}\" at {DateTime.Now:hh:mm:ss.FFF}: {newValue}";
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => CharacteristicLatestValue.Text = message);
        }

        private string FormatValueByPresentation(GattCharacteristic characteristic, IBuffer buffer)
        {
            // BT_Code: Choose a presentation format.
            GattPresentationFormat presentationFormat = null;
            if (characteristic.PresentationFormats.Count == 1)
            {
                // Get the presentation format since there's only one way of presenting it
                presentationFormat = characteristic.PresentationFormats[0];
            }
            else if (characteristic.PresentationFormats.Count > 1)
            {
                // It's difficult to figure out how to split up a characteristic and encode its different parts properly.
                // This sample doesn't try. It just encodes the whole thing to a string to make it easy to print out.
            }

            // BT_Code: For the purpose of this sample, this function converts only UInt32 and
            // UTF-8 buffers to readable text. It can be extended to support other formats if your app needs them.
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            if (presentationFormat != null)
            {
                if (presentationFormat.FormatType == GattPresentationFormatTypes.UInt32 && data.Length >= 4)
                {
                    return BitConverter.ToInt32(data, 0).ToString();
                }
                else if (presentationFormat.FormatType == GattPresentationFormatTypes.Utf8)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(data);
                    }
                    catch (ArgumentException)
                    {
                        return "(error: Invalid UTF-8 string)";
                    }
                }
                else
                {
                    // Add support for other format types as needed.
                    return "Unsupported format: " + CryptographicBuffer.EncodeToHexString(buffer);
                }
            }
            else if (data.Length == 0)
            {
                return "<empty data>";
            }
            // We don't know what format to use. Let's try some well-known profiles.
            else if (characteristic.Uuid.Equals(GattCharacteristicUuids.HeartRateMeasurement))
            {
                try
                {
                    return "Heart Rate: " + ParseHeartRateValue(data).ToString();
                }
                catch (ArgumentException)
                {
                    return "Heart Rate: (unable to parse)";
                }
            }
            else if (characteristic.Uuid.Equals(GattCharacteristicUuids.BatteryLevel))
            {
                // battery level is encoded as a percentage value in the first byte according to
                // https://www.bluetooth.com/specifications/gatt/viewer?attributeXmlFile=org.bluetooth.characteristic.battery_level.xml
                return "Battery Level: " + data[0].ToString() + "%";
            }
            // This is our custom calc service Result UUID. Format it like an Int
            else if (characteristic.Uuid.Equals(Constants.ResultCharacteristicUuid) ||
                characteristic.Uuid.Equals(Constants.BackgroundResultUuid))
            {
                return BitConverter.ToInt32(data, 0).ToString();
            }
            else
            {
                // Okay, so maybe UTF-8?
                try
                {
                    return "Unknown format: " + Encoding.UTF8.GetString(data);
                }
                catch (Exception)
                {
                    // Nope, not even UTF-8. Just show hex.
                    return "Unknown format: " + CryptographicBuffer.EncodeToHexString(buffer);
                }
            }
        }

        /// <summary>
        /// Process the raw data received from the device into application usable data,
        /// according the the Bluetooth Heart Rate Profile.
        /// https://www.bluetooth.com/specifications/gatt/viewer?attributeXmlFile=org.bluetooth.characteristic.heart_rate_measurement.xml&u=org.bluetooth.characteristic.heart_rate_measurement.xml
        /// This function throws an exception if the data cannot be parsed.
        /// </summary>
        /// <param name="data">Raw data received from the heart rate monitor.</param>
        /// <returns>The heart rate measurement value.</returns>
        private static ushort ParseHeartRateValue(byte[] data)
        {
            // Heart Rate profile defined flag values
            const byte heartRateValueFormat16 = 0x01;

            byte flags = data[0];
            bool isHeartRateValueSizeLong = ((flags & heartRateValueFormat16) != 0);

            if (isHeartRateValueSizeLong)
            {
                return BitConverter.ToUInt16(data, 1);
            }
            else
            {
                return data[1];
            }
        }
    }
}
