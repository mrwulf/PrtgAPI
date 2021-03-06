﻿. $PSScriptRoot\Support\UnitTest.ps1

Describe "Get-Group" -Tag @("PowerShell", "UnitTest") {

    It "can deserialize" {
        $groups = Get-Group
        $groups.Count | Should Be 1
    }

    It "can filter by status" {
        $items = (GetItem),(GetItem),(GetItem),(GetItem)
        $items.Count | Should Be 4

        $items[0].StatusRaw = "5" # Down
        $items[1].StatusRaw = "3" # Up
        $items[2].StatusRaw = "3" # Up
        $items[3].StatusRaw = "8" # PausedByDependency

        WithItems $items {
            $groups = Get-Group -Status Up,Paused

            $groups.Count | Should Be 3
        }
    }

    It "can filter valid wildcards" {
        $obj1 = GetItem
        $obj2 = GetItem

        $obj2.Tags = "testbananatest"

        WithItems ($obj1, $obj2) {
            $groups = Get-Group -Tags *banana*
            $groups.Count | Should Be 1
        }
    }

    It "can ignore invalid wildcards" {
        $obj1 = GetItem
        $obj2 = GetItem

        $obj2.Tags = "testbananatest"

        WithItems ($obj1, $obj2) {
            $groups = Get-Group -Tags *apple*
            $groups.Count | Should Be 0
        }
    }

    Context "Group Recursion" {
        It "retrieves groups from a uniquely named group" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupUniqueGroup"

            $groups = Get-Group | Get-Group *

            $groups.Count | Should Be 1
        }

        It "retrieves groups from a group with a duplicated name" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupDuplicateGroup"

            $groups = Get-Group | Get-Group *

            $groups.Count | Should Be 1
        }

        It "retrieves groups from a uniquely named group containing child groups" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupUniqueChildGroup"

            $groups = Get-Group | Get-Group *

            $groups.Count | Should Be 2
        }

        It "retrieves groups from a group with a duplicated name containing child groups" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupDuplicateChildGroup"

            $groups = Get-Group | Get-Group *

            $groups.Count | Should Be 2
        }

        It "retrieves only one level of groups with -Recurse:`$false" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupNoRecurse"

            $groups = Get-Group | Get-Group * -Recurse:$false

            $groups.Count | Should Be 1
        }

        It "retrieves groups from a hierarchy 6 levels deep" {
            SetResponseAndClientWithArguments "RecursiveRequestResponse" "GroupDeepNesting"

            $groups = Get-Group | Get-Group *

            $groups.Count | Should Be 33
        }
    }
}