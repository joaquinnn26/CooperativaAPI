using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CooperativaAPI.Data
{
    public static class ConceptosBeneficios
    {
        public static JArray Validar(string json, decimal total, JArray exportados)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 15000) throw new ArgumentException("Indicá ConceptosJson (hasta 15000 caracteres).");
            JArray conceptos;
            try { conceptos=JArray.Parse(json); } catch(JsonException) { throw new ArgumentException("ConceptosJson debe ser un arreglo JSON válido."); }
            if(conceptos.Count>50) throw new ArgumentException("La factura admite hasta 50 conceptos adicionales.");
            var vistos=new System.Collections.Generic.HashSet<string>();
            foreach(var token in conceptos)
            {
                var c=token as JObject;
                if(c==null) throw new ArgumentException("Cada concepto debe ser un objeto.");
                string tipo=(string)c["Tipo"],descripcion=(string)c["Descripcion"];
                if(!new[]{"SEPELIO","ORTOPEDIA","OTRO"}.Contains(tipo) || string.IsNullOrWhiteSpace(descripcion) || descripcion.Length>200 || c["Importe"]==null || !new[]{JTokenType.Integer,JTokenType.Float}.Contains(c["Importe"].Type)) throw new ArgumentException("Revisá tipo, descripción e importe de los conceptos.");
                decimal importe;
                try { importe=(decimal)c["Importe"]; } catch(Exception ex) when(ex is OverflowException || ex is FormatException) { throw new ArgumentException("Importe de concepto inválido."); }
                if(Math.Abs(importe)>9999999999.99m || importe!=decimal.Round(importe,2)) throw new ArgumentException("El concepto debe tener hasta dos decimales y un importe válido.");
                if(tipo!="OTRO")
                {
                    var esperado=exportados?.OfType<JObject>().SingleOrDefault(x=>(string)x["Tipo"]==tipo);
                    if(!vistos.Add(tipo) || esperado==null || importe!=(decimal)esperado["Importe"]) throw new ArgumentException("El cargo de "+tipo+" no coincide con la adhesión exportada o está duplicado.");
                }
            }
            if(exportados!=null && exportados.OfType<JObject>().Any(c=>!vistos.Contains((string)c["Tipo"]))) throw new ArgumentException("Faltan cuotas de adhesiones incluidas en el padrón exportado.");
            if(conceptos.Sum(c=>(decimal)c["Importe"])>total) throw new ArgumentException("El subtotal de otros conceptos supera el total de la factura.");
            // MontoTotal ya incluye estos cargos: nunca se vuelven a sumar.
            return conceptos;
        }
    }
}
