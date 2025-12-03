using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentGateway;

public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
   public static readonly string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

   public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
   {
      if (reader.TokenType == JsonTokenType.String && 
          DateTimeOffset.TryParseExact(reader.GetString(), Format, null, System.Globalization.DateTimeStyles.None, out DateTimeOffset result))
      {
         return result;
      }
      
      throw new JsonException($"Unable to parse DateTimeOffset with format '{Format}'.");
   }

   public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
   {
      writer.WriteStringValue(value.ToString(Format));
   }
}

