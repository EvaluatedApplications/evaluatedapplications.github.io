using Phasor;

// Encode a number and some symbols
double[] five = PhasorCodec.Encode("5");
double[] three = PhasorCodec.Encode("3");
double[] age = PhasorCodec.Encode("age");
double[] name = PhasorCodec.Encode("name");
double[] hello = PhasorCodec.Encode("hello");

// Bind two numbers: adds them on the linear band
double[] fivePlusThree = PhasorCodec.Bind(five, three);
Console.WriteLine($"5 + 3 = {PhasorCodec.DecodeSum(fivePlusThree, max: 10)}");

// Bind each value to a role, bundle the pairs into one record, then unbind a role to read its value back
double[] record = PhasorCodec.Bundle(PhasorCodec.Bind(age, five), PhasorCodec.Bind(name, hello));
double[] ageField = PhasorCodec.Unbind(record, age);
Console.WriteLine($"age field matches 5 better than 3: {PhasorCodec.Correlate(ageField, five) > PhasorCodec.Correlate(ageField, three)}");

// For larger workloads and similarity search, HoloCodec is SIMD and configurable in width
var codec = new HoloCodec(dim: 1024);
double[] encoded = codec.Encode("hello");
Console.WriteLine($"HoloCodec self-similarity is positive: {HoloCodec.Dot(encoded, codec.Encode("hello")) > 0}");
