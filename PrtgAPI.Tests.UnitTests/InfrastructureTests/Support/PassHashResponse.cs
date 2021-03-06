﻿using System;
using System.Threading.Tasks;

namespace PrtgAPI.Tests.UnitTests.InfrastructureTests.Support
{
    class PassHashResponse : IWebResponse
    {
        private string response;

        public PassHashResponse(string response = "12345678")
        {
            this.response = response;
        }

        public string GetResponseText(ref string address)
        {
            return response;
        }
    }
}
