using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CooperativaAPI.DTOs
{
    public class LoginRequest
    {
        public string TipoAcceso { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
