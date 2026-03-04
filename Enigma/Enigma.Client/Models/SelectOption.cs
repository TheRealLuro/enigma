namespace Enigma.Client.Models;

public sealed record SelectOption<TValue>(TValue Value, string Label);
