# Proyecto-BLE
Monitorizacion de variables de un dispositvo via bluetooth Low Energy

## Auto-Connect Feature

The Scenario2_Client page now supports automatic connection when a device ID is passed during navigation.

### How to use:

To enable auto-connect when navigating to Scenario2_Client, save the selected device's ID to the App's SelectedDeviceId property before navigation. This is typically done in a device selection handler, such as a ListView.SelectionChanged event.

**Example snippet:**

```csharp
// In your device selection handler (e.g., ListView.SelectionChanged)
private void DeviceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (e.AddedItems.Count > 0)
    {
        var selectedDevice = e.AddedItems[0] as DeviceInformation;
        if (selectedDevice != null)
        {
            // Save the device ID to the App instance
            ((App)Application.Current).SelectedDeviceId = selectedDevice.Id;
            
            // Now navigate to Scenario2_Client
            // The navigation logic in MainPage will automatically pass the device ID
            // and Scenario2_Client will auto-connect
        }
    }
}
```

The MainPage navigation logic checks if you're navigating to Scenario2_Client and automatically passes the SelectedDeviceId if available. The Scenario2_Client page will then automatically attempt to connect to the device on load.


