using Hishab.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hishab.Infrastructure.Persistence;

/// <summary>Stores <see cref="Money"/> as a plain numeric amount; the currency is always BDT.</summary>
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(m => m.Amount, v => Money.Bdt(v, "amount"));
