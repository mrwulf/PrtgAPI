﻿. $PSScriptRoot\Support\IntegrationTestSafe.ps1

Describe "Get-Device_IT" {
    It "has correct number of devices" {
        $devices = Get-Device

        $devices.Count | Should Be (Settings DevicesInTestServer)
    }

    It "can filter by name" {
        $devices = Get-Device (Settings DeviceName)

        $devices.Count | Should BeGreaterThan 0

        foreach($device in $devices)
        {
            $device.Name | Should Be (Settings DeviceName)
        }
    }

    It "can filter by starting wildcard" {
        $devices = Get-Device Probe*

        $devices.Count | Should BeGreaterThan 0

        foreach($device in $devices)
        {
            $device.Name | Should BeLike "Probe*"
        }
    }

    It "can filter by ending wildcard" {
        $devices = Get-Device *Device

        $devices.Count | Should BeGreaterThan 0

        foreach($device in $devices)
        {
            $device.Name | Should BeLike "*Device"
        }
    }

    It "can filter by wildcard contains" {
        $devices = Get-Device *prtg*

        $devices.Count | Should BeGreaterThan 0

        foreach($device in $devices)
        {
            $device.Name | Should BeLike "*prtg*"
        }
    }

    It "can filter by Id" {
        $device = Get-Device -Id (Settings Device)

        $device.Count | Should Be 1
        $device.Id | Should Be (Settings Device)
    }

    It "can filter by tags" {
        $device = Get-Device -Tags (Settings DeviceTag)

        $device.Count | Should Be 1
    }

    It "can pipe from groups" {

        $group = Get-Group -Id (Settings Group)

        $devices = $group | Get-Device -Recurse:$false

        $devices.Count | Should be (Settings DevicesInTestGroup)
    }

    It "can pipe from probes" {
        $probe = Get-Probe -Id (Settings Probe)

        $devices = $probe | Get-Device

        $devices.Count | Should Be (Settings DevicesInTestProbe)
    }

    It "can pipe from search filters" {
        $devices = New-SearchFilter name contains probe | Get-Device

        $devices.Count | Should Be 1
    }

    It "can recursively retrieve devices from a group" {
        $count = (Settings DevicesInTestGroup) + 1

        $devices = Get-Group -Id (Settings Group) | Get-Device

        ($devices.Count) | Should Be $count
    }
}