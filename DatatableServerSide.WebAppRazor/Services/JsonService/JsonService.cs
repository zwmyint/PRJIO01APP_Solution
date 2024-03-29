﻿using System.Text;
using System.Text.Json;

namespace DatatableServerSide.WebAppRazor.Services.JsonService
{
    public class JsonService : IJsonService
    {
        public byte[] Write<T>(IList<T> registers)
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(registers));
        }
    }
}
