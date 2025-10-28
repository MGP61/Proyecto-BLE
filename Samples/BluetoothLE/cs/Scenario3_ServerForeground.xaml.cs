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
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SDKTemplate
{
    // This scenario declares support for a calculator service.
    // Remote clients (including this sample on another machine) can supply:
    // - Operands 1 and 2
    // - an operator (+,-,*,/)
    // and get a result
    public sealed partial class Scenario3_ServerForeground : Page
    {
        private MainPage rootPage = MainPage.Current;

        // Managing the service.
        private GattServiceProvider serviceProvider;
        private GattLocalCharacteristic operand1Characteristic;
        private GattLocalCharacteristic operand2Characteristic;
        private GattLocalCharacteristic operatorCharacteristic;
        private GattLocalCharacteristic resultCharacteristic;
        private GattServiceProviderAdvertisingParameters advertisingParameters;

        // Implementing the service.
        private int operand1Value = 0;
        private int operand2Value = 0;
        CalculatorOperators operatorValue = 0;
        private int resultValue = 0;

        private bool navigatedTo = false;
        private bool startingService = false; // reentrancy protection

        private enum CalculatorCharacteristics
        {
            Operand1 = 1,
            Operand2 = 2,
            Operator = 3,
        }

        private enum CalculatorOperators
        {
            Add = 1,
            Subtract = 2,
            Multiply = 3,
            Divide = 4,
        }

        #region UI Code
        public Scenario3_ServerForeground()
        {
            InitializeComponent();

            advertisingParameters = new GattServiceProviderAdvertisingParameters
            {
                // IsDiscoverable determines whether a remote device can query the local device for support
                // of this service
                IsDiscoverable = true
            };

            ServiceIdRun.Text = Constants.CalculatorServiceUuid.ToString();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            navigatedTo = true;

            // BT_Code: New for Creator's Update - Bluetooth adapter has properties of the local BT radio.
            BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync();

            if (adapter != null && adapter.IsPeripheralRoleSupported)
            {
                // BT_Code: Specify that the server advertises as connectable.
                // IsConnectable determines whether a call to publish will attempt to start advertising and
                // put the service UUID in the ADV packet (best effort)
                advertisingParameters.IsConnectable = true;

                ServerPanel.Visibility = Visibility.Visible;
            }
            else
            {
                // No Bluetooth adapter or adapter cannot act as server.
                PeripheralWarning.Visibility = Visibility.Visible;
            }

            // Check whether the local Bluetooth adapter and Windows support 2M and Coded PHY.
            if (!FeatureDetection.AreExtendedAdvertisingPhysAndScanParametersSupported)
            {
                Publishing2MPHYReasonRun.Text = "(Not supported by this version of Windows)";
            }
            else if (adapter != null && adapter.IsLowEnergyUncoded2MPhySupported)
            {
                Publishing2MPHY.IsEnabled = true;
            }
            else
            {
                Publishing2MPHYReasonRun.Text = "(Not supported by default Bluetooth adapter)";
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigatedTo = false;

            UnsubscribeServiceEvents();
            // Do not null out the characteristics because tasks may still be using them.

            if (serviceProvider != null)
            {
                if (serviceProvider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Stopped)
                {
                    serviceProvider.StopAdvertising();
                }
                serviceProvider = null;
            }
        }

        private async void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            if (serviceProvider == null)
            {
                // Server not initialized yet - initialize it and start publishing
                // Don't try to start if already starting.
                if (startingService)
                {
                    return;
                }
                PublishButton.Content = "Starting...";
                startingService = true;
                await CreateAndAdvertiseServiceAsync();
                startingService = false;
                if (serviceProvider != null)
                {
                    rootPage.NotifyUser("Service successfully started", NotifyType.StatusMessage);
                }
                else
                {
                    UnsubscribeServiceEvents();
                    rootPage.NotifyUser("Service not started", NotifyType.ErrorMessage);
                }
            }
            else
            {
                // BT_Code: Stops advertising support for custom GATT Service
                UnsubscribeServiceEvents();
                serviceProvider.StopAdvertising();
                serviceProvider = null;
            }
            PublishButton.Content = serviceProvider == null ? "Start Service": "Stop Service";
        }

        private void Publishing2MPHY_Click(object sender, RoutedEventArgs e)
        {
            // Update the advertising parameters based on the checkbox.
            bool shouldAdvertise2MPHY = Publishing2MPHY.IsChecked.Value;
            advertisingParameters.UseLowEnergyUncoded1MPhyAsSecondaryPhy = !shouldAdvertise2MPHY;
            advertisingParameters.UseLowEnergyUncoded2MPhyAsSecondaryPhy = shouldAdvertise2MPHY;

            if (serviceProvider != null)
            {
                // Reconfigure the advertising parameters on the fly.
                serviceProvider.UpdateAdvertisingParameters(advertisingParameters);
            }
        }

        private void UnsubscribeServiceEvents()
        {
            if (operand1Characteristic != null)
            {
                operand1Characteristic.WriteRequested -= Op1Characteristic_WriteRequestedAsync;
            }
            if (operand2Characteristic != null)
            {
                operand2Characteristic.WriteRequested -= Op2Characteristic_WriteRequestedAsync;
            }
            if (operatorCharacteristic != null)
            {
                operatorCharacteristic.WriteRequested -= OperatorCharacteristic_WriteRequestedAsync;
            }
            if (resultCharacteristic != null)
            {
                resultCharacteristic.ReadRequested -= ResultCharacteristic_ReadRequestedAsync;
                resultCharacteristic.SubscribedClientsChanged -= ResultCharacteristic_SubscribedClientsChanged;
            }
            if (serviceProvider != null)
            {
                serviceProvider.AdvertisementStatusChanged -= ServiceProvider_AdvertisementStatusChanged;
            }
        }

        private void UpdateUI()
        {
            string operationText = "N/A";
            switch (operatorValue)
            {
                case CalculatorOperators.Add:
                    operationText = "+";
                    break;
                case CalculatorOperators.Subtract:
                    operationText = "\u2212"; // Minus sign
                    break;
                case CalculatorOperators.Multiply:
                    operationText = "\u00d7"; // Multiplication sign
                    break;
                case CalculatorOperators.Divide:
                    operationText = "\u00f7"; // Division sign
                    break;
            }
            OperationTextBox.Text = operationText;
            Operand1TextBox.Text = operand1Value.ToString();
            Operand2TextBox.Text = operand2Value.ToString();
            ResultTextBox.Text = resultValue.ToString();
        }
        #endregion

        /// <summary>
        /// Uses the relevant Service/Characteristic UUIDs to initialize, hook up event handlers and start a service on the local system.
        /// </summary>
        /// <returns></returns>
        private async Task CreateAndAdvertiseServiceAsync()
        {
            // BT_Code: Initialize and starting a custom GATT Service using GattServiceProvider.
            GattServiceProviderResult serviceResult = await GattServiceProvider.CreateAsync(Constants.CalculatorServiceUuid);
            if (serviceResult.Error != BluetoothError.Success)
            {
                rootPage.NotifyUser($"Could not create service provider: {serviceResult.Error}", NotifyType.ErrorMessage);
                return;
            }
            GattServiceProvider provider = serviceResult.ServiceProvider;

            GattLocalCharacteristicResult result = await provider.Service.CreateCharacteristicAsync(
                Constants.Operand1CharacteristicUuid, Constants.gattOperand1Parameters);
            if (result.Error != BluetoothError.Success)
            {
                rootPage.NotifyUser($"Could not create operand1 characteristic: {result.Error}", NotifyType.ErrorMessage);
                return;
            }
            operand1Characteristic = result.Characteristic;
            operand1Characteristic.WriteRequested += Op1Characteristic_WriteRequestedAsync;

            result = await provider.Service.CreateCharacteristicAsync(
                Constants.Operand2CharacteristicUuid, Constants.gattOperand2Parameters);
            if (result.Error != BluetoothError.Success)
            {
                rootPage.NotifyUser($"Could not create operand2 characteristic: {result.Error}", NotifyType.ErrorMessage);
                return;
            }

            operand2Characteristic = result.Characteristic;
            operand2Characteristic.WriteRequested += Op2Characteristic_WriteRequestedAsync;

            result = await provider.Service.CreateCharacteristicAsync(
                Constants.OperatorCharacteristicUuid, Constants.gattOperatorParameters);
            if (result.Error != BluetoothError.Success)
            {
                rootPage.NotifyUser($"Could not create operator characteristic: {result.Error}", NotifyType.ErrorMessage);
                return;
            }

            operatorCharacteristic = result.Characteristic;
            operatorCharacteristic.WriteRequested += OperatorCharacteristic_WriteRequestedAsync;

            result = await provider.Service.CreateCharacteristicAsync(Constants.ResultCharacteristicUuid, Constants.gattResultParameters);
            if (result.Error != BluetoothError.Success)
            {
                rootPage.NotifyUser($"Could not create result characteristic: {result.Error}", NotifyType.ErrorMessage);
                return;
            }

            resultCharacteristic = result.Characteristic;
            resultCharacteristic.ReadRequested += ResultCharacteristic_ReadRequestedAsync;
            resultCharacteristic.SubscribedClientsChanged += ResultCharacteristic_SubscribedClientsChanged;

            // The advertising parameters were updated at various points in this class.
            // IsDiscoverable was set in the class constructor.
            // IsConnectable was set in OnNavigatedTo when we confirmed that the device supports peripheral role.
            // UseLowEnergyUncoded1MPhy/2MPhyAsSecondaryPhy was set when the user toggled the Publishing2MPHY button.

            // Last chance: Did the user navigate away while we were doing all this work?
            // If so, then abandon our work without starting the provider.
            // Must do this after the last await. (Could also do it after earlier awaits.)
            if (!navigatedTo)
            {
                return;
            }

            provider.AdvertisementStatusChanged += ServiceProvider_AdvertisementStatusChanged;
            provider.StartAdvertising(advertisingParameters);

            // Let the other methods know that we have a provider that is advertising.
            serviceProvider = provider;
        }

        private void ResultCharacteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            rootPage.NotifyUser($"New device subscribed. New subscribed count: {sender.SubscribedClients.Count}", NotifyType.StatusMessage);
        }

        private void ServiceProvider_AdvertisementStatusChanged(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
        {
            // Created - The default state of the advertisement, before the service is published for the first time.
            // Stopped - Indicates that the application has canceled the service publication and its advertisement.
            // Started - Indicates that the system was successfully able to issue the advertisement request.
            // Aborted - Indicates that the system was unable to submit the advertisement request, or it was canceled due to resource contention.

            rootPage.NotifyUser($"New Advertisement Status: {sender.AdvertisementStatus}", NotifyType.StatusMessage);
        }

        private async void ResultCharacteristic_ReadRequestedAsync(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
        {
            // BT_Code: Process a read request.
            using (args.GetDeferral())
            {
                // Get the request information.  This requires device access before an app can access the device's request.
                GattReadRequest request = await args.GetRequestAsync();
                if (request == null)
                {
                    // No access allowed to the device.  Application should indicate this to the user.
                    rootPage.NotifyUser("Access to device not allowed", NotifyType.ErrorMessage);
                    return;
                }

                // Can get details about the request such as the size and offset, as well as monitor the state to see if it has been completed/cancelled externally.
                // request.Offset
                // request.Length
                // request.State
                // request.StateChanged += <Handler>

                // Gatt code to handle the response
                request.RespondWithValue(BufferHelpers.BufferFromInt32(resultValue));
            }
        }

        private void ComputeResult()
        {
            switch (operatorValue)
            {
                case CalculatorOperators.Add:
                    resultValue = operand1Value + operand2Value;
                    break;
                case CalculatorOperators.Subtract:
                    resultValue = operand1Value - operand2Value;
                    break;
                case CalculatorOperators.Multiply:
                    resultValue = operand1Value * operand2Value;
                    break;
                case CalculatorOperators.Divide:
                    if (operand2Value == 0 || (operand1Value == Int32.MinValue && operand2Value == -1))
                    {
                        rootPage.NotifyUser("Division overflow", NotifyType.ErrorMessage);
                    }
                    else
                    {
                        resultValue = operand1Value / operand2Value;
                    }
                    break;
                default:
                    rootPage.NotifyUser("Invalid Operator", NotifyType.ErrorMessage);
                    break;
            }
            NotifyClientDevices(resultValue);
        }

        private async void NotifyClientDevices(int computedValue)
        {
            // BT_Code: Returns a collection of all clients that the notification was attempted and the result.
            IReadOnlyList<GattClientNotificationResult> results = await resultCharacteristic.NotifyValueAsync(BufferHelpers.BufferFromInt32(computedValue));

            rootPage.NotifyUser($"Sent value {computedValue} to clients.", NotifyType.StatusMessage);
            foreach (var result in results)
            {
                // An application can iterate through each registered client that was notified and retrieve the results:
                //
                // result.SubscribedClient: The details on the remote client.
                // result.Status: The GattCommunicationStatus
                // result.ProtocolError: iff Status == GattCommunicationStatus.ProtocolError
            }
        }

        private async void Op1Characteristic_WriteRequestedAsync(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            // BT_Code: Processing a write request.
            using (args.GetDeferral())
            {
                // Get the request information.  This requires device access before an app can access the device's request.
                GattWriteRequest request = await args.GetRequestAsync();
                if (request == null)
                {
                    // No access allowed to the device.  Application should indicate this to the user.
                    return;
                }
                ProcessWriteCharacteristic(request, CalculatorCharacteristics.Operand1);
            }
        }

        private async void Op2Characteristic_WriteRequestedAsync(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            using (args.GetDeferral())
            {
                // Get the request information.  This requires device access before an app can access the device's request.
                GattWriteRequest request = await args.GetRequestAsync();
                if (request == null)
                {
                    // No access allowed to the device.  Application should indicate this to the user.
                    return;
                }
                ProcessWriteCharacteristic(request, CalculatorCharacteristics.Operand2);
            }
        }

        private async void OperatorCharacteristic_WriteRequestedAsync(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            using (args.GetDeferral())
            {
                // Get the request information.  This requires device access before an app can access the device's request.
                GattWriteRequest request = await args.GetRequestAsync();
                if (request == null)
                {
                    // No access allowed to the device.  Application should indicate this to the user.
                }
                else
                {
                    ProcessWriteCharacteristic(request, CalculatorCharacteristics.Operator);
                }
            }
        }

        /// <summary>
        /// BT_Code: Processing a write request.Takes in a GATT Write request and updates UX based on opcode.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="opCode">Operand (1 or 2) and Operator (3)</param>
        private async void ProcessWriteCharacteristic(GattWriteRequest request, CalculatorCharacteristics opCode)
        {
            int? val = BufferHelpers.Int32FromBuffer(request.Value);

            if (val == null)
            {
                // Input is the wrong length. Respond with a protocol error if requested.
                if (request.Option == GattWriteOption.WriteWithResponse)
                {
                    request.RespondWithProtocolError(GattProtocolError.InvalidAttributeValueLength);
                }
                return;
            }

            switch (opCode)
            {
                case CalculatorCharacteristics.Operand1:
                    operand1Value = val.Value;
                    break;
                case CalculatorCharacteristics.Operand2:
                    operand2Value = val.Value;
                    break;
                case CalculatorCharacteristics.Operator:
                    if (!Enum.IsDefined(typeof(CalculatorOperators), val.Value))
                    {
                        if (request.Option == GattWriteOption.WriteWithResponse)
                        {
                            request.RespondWithProtocolError(GattProtocolError.InvalidPdu);
                        }
                        return;
                    }
                    operatorValue = (CalculatorOperators)val.Value;
                    break;
            }
            // Complete the request if needed
            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }

            ComputeResult();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateUI);
        }
    }
}
