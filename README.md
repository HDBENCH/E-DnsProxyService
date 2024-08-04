# E-DnsProxyService

E-DnsProxyService accepts or rejects any DNS request.

## How to use

1. Copy the files to the installation folder of your choice.<BR>
DnsProxyAdmin.exe<BR>
DnsProxyAdmin.exe.config<BR>
DnsProxyInstaller.exe<BR>
DnsProxyInstaller.exe.config<BR>
DnsProxyService.exe<BR>
DnsProxyService.exe.config<BR>
2. Run DnsProxyInstaller.exe in the installation folder.
3. Entering 1 will install E-DnsProxyService.
4. Entering 3 will start the E-DnsProxyService.
4. Set the DNS address to 127.0.0.1 in your network settings.
5. Run DnsProxyAdmin.exe. A list of DNS requests will be displayed, so right-click to accept or reject them.

## If it doesn't work properly
1. Run DnsProxyInstaller.exe in the installation folder.
2. Entering 4 will stop the E-DnsProxyService.
3. Using a text editor, open config.ini in the installation folder.
4. Change dns_server=8.8.8.8 to the address of your router, e.g. dns_server=192.168.0.1
5. Entering 3 will start the E-DnsProxyService.
