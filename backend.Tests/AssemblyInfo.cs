// Every test class calls AdmissionWebApplicationFactory.ResetDatabase against one shared PostgreSQL
// database, so xUnit's class-level parallelism makes one class drop the schema while another is
// querying it (spurious 503s). Serialize the whole assembly.
//
// This lives in its own file on purpose: the classes task adds the same assembly attribute inside
// its own test file, and two [assembly: CollectionBehavior] attributes in one assembly are a CS0579
// duplicate-attribute build error. At merge time exactly one of the two must survive.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
