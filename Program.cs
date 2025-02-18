using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;

namespace AwsEc2NiceDcvPoc
{
    class Program
    {
        private static readonly Dictionary<string, string> LinuxAmis = new Dictionary<string, string>
        {
            { "nano", "ami-0c55b159cbfafe1f0" },
            { "large", "ami-0b69ea66ff7391e80" }
        };

        private static readonly Dictionary<string, string> WindowsAmis = new Dictionary<string, string>
        {
            { "nano", "ami-061392db613a6357a" },
            { "large", "ami-0b6a8a1b9c4febf14" }
        };

        private static readonly Dictionary<string, string> InstanceTypes = new Dictionary<string, string>
        {
            { "nano", "t2.nano" },
            { "large", "t2.large" }
        };

        static async Task Main(string[] args)
        {
            Console.WriteLine("AWS EC2 NICE DCV POC Launcher z wykorzystaniem zmiennych środowiskowych");
            Console.WriteLine("-----------------------------------------------------------------------");

            string keyPairName = Environment.GetEnvironmentVariable("AWS_KEYPAIR_NAME");
            string securityGroupId = Environment.GetEnvironmentVariable("AWS_SECURITY_GROUP_ID");

            if (string.IsNullOrEmpty(keyPairName) || string.IsNullOrEmpty(securityGroupId))
            {
                Console.WriteLine("Brak wymaganych zmiennych środowiskowych (AWS_KEYPAIR_NAME, AWS_SECURITY_GROUP_ID).");
                return;
            }

            Console.WriteLine($"Używana Key Pair: {keyPairName}");
            Console.WriteLine($"Używany Security Group ID: {securityGroupId}");

            Console.WriteLine("Wybierz system operacyjny:");
            Console.WriteLine("1: Linux (z NICE DCV)");
            Console.WriteLine("2: Windows (z NICE DCV - wymaga dodatkowej konfiguracji)");
            Console.Write("Twój wybór (1 lub 2): ");
            string osChoice = Console.ReadLine();

            string osType;
            if (osChoice == "1")
                osType = "Linux";
            else if (osChoice == "2")
                osType = "Windows";
            else
            {
                Console.WriteLine("Nieprawidłowy wybór. Zakończenie.");
                return;
            }

            Console.WriteLine("Wybierz rozmiar instancji:");
            Console.WriteLine("1: Nano");
            Console.WriteLine("2: Large");
            Console.Write("Twój wybór (1 lub 2): ");
            string sizeChoice = Console.ReadLine();

            string sizeType;
            if (sizeChoice == "1")
                sizeType = "nano";
            else if (sizeChoice == "2")
                sizeType = "large";
            else
            {
                Console.WriteLine("Nieprawidłowy wybór. Zakończenie.");
                return;
            }

            string amiId = osType == "Linux" ? LinuxAmis[sizeType] : WindowsAmis[sizeType];
            string instanceType = InstanceTypes[sizeType];

            string userData = "";
            if (osType == "Linux")
            {
                userData = @"#!/bin/bash
yum update -y
yum install -y dcv-server dcv-gl-renderer dcv-web-viewer
dcv create-session --type virtual-session --owner ec2-user my-session
";
            }
            else
            {
                userData = @"<powershell>
Write-Output 'Instalacja NICE DCV na Windows...'
</powershell>";
            }

            Console.WriteLine($"Tworzenie instancji {osType} typu {instanceType} z AMI {amiId}...");

            var ec2Client = new AmazonEC2Client(RegionEndpoint.USEast1);
            var runRequest = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = instanceType,
                MinCount = 1,
                MaxCount = 1,
                UserData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userData)),
                KeyName = keyPairName,
                SecurityGroupIds = new List<string> { securityGroupId }
            };

            try
            {
                var runResponse = await ec2Client.RunInstancesAsync(runRequest);
                var instanceId = runResponse.Reservation.Instances[0].InstanceId;
                Console.WriteLine($"Instancja uruchomiona, ID: {instanceId}");

                Console.WriteLine("Oczekiwanie na stan Running...");
                await WaitForInstanceRunning(ec2Client, instanceId);

                var descResponse = await ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest
                {
                    InstanceIds = new List<string> { instanceId }
                });
                var instance = descResponse.Reservations[0].Instances[0];
                string publicDns = instance.PublicDnsName;

                Console.WriteLine("Instancja działa.");
                Console.WriteLine($"Publiczny DNS: {publicDns}");

                string dcvUrl = $"https://{publicDns}:8443/";
                Console.WriteLine();
                Console.WriteLine("URL do sesji NICE DCV (do osadzenia w iframe):");
                Console.WriteLine(dcvUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Wystąpił błąd podczas uruchamiania instancji:");
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task WaitForInstanceRunning(IAmazonEC2 ec2Client, string instanceId)
        {
            bool running = false;
            while (!running)
            {
                var response = await ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest
                {
                    InstanceIds = new List<string> { instanceId }
                });

                var state = response.Reservations[0].Instances[0].State.Name;
                Console.WriteLine($"Aktualny stan: {state}");
                if (state == InstanceStateName.Running)
                {
                    running = true;
                }
                else
                {
                    Thread.Sleep(5000);
                }
            }
        }
    }
}