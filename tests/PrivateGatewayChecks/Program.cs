using Archer.PrivateGateway;
using Archer.RemoteGateway;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS: "+name); }
using var owner=ECDsa.Create(ECCurve.NamedCurves.nistP256);
var parameters=owner.ExportParameters(false);
var raw=new byte[]{4}.Concat(parameters.Q.X!).Concat(parameters.Q.Y!).ToArray();
var key=Convert.ToBase64String(raw);
var fingerprint=DeviceProof.Hash(raw);
var guard=new DeviceProof(fingerprint);
var now=DateTimeOffset.UtcNow;
var body=Encoding.UTF8.GetBytes("{\"message\":\"hello\"}");
DeviceProof.Challenge Begin(string path=DeviceProof.Message)=>guard.Begin("POST",path,DeviceProof.Hash(body),now)!;
string Sign(DeviceProof.Challenge c)=>Convert.ToBase64String(owner.SignData(DeviceProof.Payload(c),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
var challenge=Begin();
Check(guard.Consume(challenge.Ticket,"POST",DeviceProof.Message,body,key,Sign(challenge),now),"approved hardware-shaped key and exact request");
Check(!guard.Consume(challenge.Ticket,"POST",DeviceProof.Message,body,key,Sign(challenge),now),"replay rejected");
challenge=Begin();
Check(!guard.Consume(challenge.Ticket,"POST",DeviceProof.Message,[1,2],key,Sign(challenge),now),"changed request body rejected");
challenge=Begin();
Check(!guard.Consume(challenge.Ticket,"POST",DeviceProof.Receipt,body,key,Sign(challenge),now),"message cannot become receipt or arbitrary route");
challenge=Begin();
Check(!guard.Consume(challenge.Ticket,"POST",DeviceProof.Message,body,key,Sign(challenge),now.AddMinutes(2)),"expired ticket rejected");
challenge=Begin();
using var attacker=ECDsa.Create(ECCurve.NamedCurves.nistP256);
var a=attacker.ExportParameters(false);
var wrong=Convert.ToBase64String(new byte[]{4}.Concat(a.Q.X!).Concat(a.Q.Y!).ToArray());
Check(!guard.Consume(challenge.Ticket,"POST",DeviceProof.Message,body,wrong,Sign(challenge),now),"unapproved device rejected");
Check(guard.Begin("POST","/api/projects",DeviceProof.Hash(body),now)==null,"no admin route proof");
Check(guard.Begin("GET",DeviceProof.Overview,DeviceProof.Hash(body),now)==null,"read proof requires empty body");
var config=new Dictionary<string,string>{
 ["ARCHER_PRIVATE_GATEWAY_ENABLED"]="true",["ARCHER_PRIVATE_HOST"]="archer.owner.ts.net",
 ["ARCHER_PRIVATE_AUTH0_DOMAIN"]="dev-example.us.auth0.com",["ARCHER_PRIVATE_NATIVE_CLIENT_ID"]="example_native_client",
 ["ARCHER_PRIVATE_OWNER_SUBJECT"]="auth0|fixture",["ARCHER_COMPANION_BRIDGE_KEY"]=new string('A',64),
 ["ARCHER_COMPANION_DEVICE_SHA256"]=fingerprint
};
var settings=PrivateSettings.Load(k=>config.GetValueOrDefault(k));
ClaimsPrincipal Claims(string subject,string client)=>new(new ClaimsIdentity(new[]{new Claim("sub",subject),new Claim("azp",client),new Claim("scope","conversation:read conversation:write")},"fixture"));
Check(Auth0OwnerClaimPolicy.AcceptValidatedClaims(Claims("auth0|fixture","example_native_client"),settings.Owner),"pinned owner and native OAuth client claim contract");
Check(!Auth0OwnerClaimPolicy.AcceptValidatedClaims(Claims("auth0|other","example_native_client"),settings.Owner),"wrong owner rejected");
Check(!Auth0OwnerClaimPolicy.AcceptValidatedClaims(Claims("auth0|fixture","wrong_client_id"),settings.Owner),"wrong OAuth client rejected");
config["ARCHER_PRIVATE_HOST"]="public.example.com";
try { PrivateSettings.Load(k=>config.GetValueOrDefault(k)); throw new Exception("Public hostname accepted"); } catch(InvalidOperationException) { Check(true,"private gateway hostname required"); }
