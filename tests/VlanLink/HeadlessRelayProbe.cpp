#include "cli_net.h"
#include "cli_log.h"
#include "cli_peer.h"
#include <iostream>
#include <algorithm>
#include <stdexcept>
#include <cstdlib>
#include <string>
#include <vector>
#include <memory>

namespace VLan { bool g_verboseLog=false; }
using namespace VLan;
static uint32_t number(const char* text,uint32_t max) {
    if(!text||!*text)throw std::runtime_error("argument");uint64_t n=0;
    for(const char* p=text;*p;++p){if(*p<'0'||*p>'9')throw std::runtime_error("argument");n=n*10+(*p-'0');if(n>max)throw std::runtime_error("argument");}return static_cast<uint32_t>(n);
}
static void put16(Buffer& b,size_t p,uint16_t n){b[p]=static_cast<uint8_t>(n>>8);b[p+1]=static_cast<uint8_t>(n);}
static void put32(Buffer& b,size_t p,uint32_t n){for(int i=0;i<4;++i)b[p+i]=static_cast<uint8_t>(n>>(24-i*8));}
static uint16_t get16(const Buffer& b,size_t p){return static_cast<uint16_t>((static_cast<uint16_t>(b[p])<<8)|b[p+1]);}
static uint32_t get32(const Buffer& b,size_t p){uint32_t n=0;for(int i=0;i<4;++i)n=(n<<8)|b[p+i];return n;}
static uint16_t checksum(const uint8_t* data,size_t size){uint32_t n=0;for(size_t i=0;i<size;i+=2)n+=(static_cast<uint16_t>(data[i])<<8)|(i+1<size?data[i+1]:0);while(n>>16)n=(n&65535)+(n>>16);return static_cast<uint16_t>(~n);}
static bool vip(uint32_t ip){return (ip&0xffffff00u)==VNET_SUBNET && (ip&255)>=2 && (ip&255)<=254;}
struct UdpSocketOwner {
    socket_t fd;
    UdpSocketOwner():fd(SOCK_INVALID){}
    ~UdpSocketOwner(){if(fd!=SOCK_INVALID)sock_close(fd);}
    bool open(){fd=socket(AF_INET,SOCK_DGRAM,IPPROTO_UDP);if(fd==SOCK_INVALID||!setNonBlocking(fd))return false;sockaddr_in bindAddr={};bindAddr.sin_family=AF_INET;bindAddr.sin_addr.s_addr=INADDR_ANY;return bind(fd,reinterpret_cast<sockaddr*>(&bindAddr),sizeof(bindAddr))==0;}
};
static std::string takeEnv(const char* name){const char* p=std::getenv(name);std::string value=p?p:"";_putenv_s(name,"");SetEnvironmentVariableA(name,nullptr);return value;}
static std::string resolve(const std::string& host){
    addrinfo hints={};hints.ai_family=AF_INET;hints.ai_socktype=SOCK_STREAM;addrinfo* result=nullptr;
    if(getaddrinfo(host.c_str(),nullptr,&hints,&result)!=0||!result)throw std::runtime_error("resolve");
    uint32_t ip=ntohl(reinterpret_cast<sockaddr_in*>(result->ai_addr)->sin_addr.s_addr);freeaddrinfo(result);return ipToString(ip);
}
static Buffer udpPacket(uint32_t src,uint32_t dst,uint16_t from,uint16_t to,const std::string& payload){
    Buffer b(28+payload.size(),0);b[0]=0x45;put16(b,2,static_cast<uint16_t>(b.size()));put16(b,4,static_cast<uint16_t>(GetTickCount()));b[8]=64;b[9]=17;put32(b,12,src);put32(b,16,dst);put16(b,10,checksum(b.data(),20));
    put16(b,20,from);put16(b,22,to);put16(b,24,static_cast<uint16_t>(8+payload.size()));
    // IPv4 permits UDP checksum zero. The IPv4 header checksum is always supplied.
    std::copy(payload.begin(),payload.end(),b.begin()+28);return b;
}
static bool matchesEcho(const Buffer& b,uint32_t host,uint32_t self,uint16_t target,uint16_t local,const std::string& payload){
    if(b.size()<28||(b[0]>>4)!=4||b[9]!=17)return false;size_t h=(b[0]&15)*4;
    if(h<20||b.size()<h+8||get16(b,2)!=b.size()||checksum(b.data(),h)!=0||(get16(b,6)&0x3fffu)!=0)return false;
    if(get32(b,12)!=host||get32(b,16)!=self||get16(b,h)!=target||get16(b,h+2)!=local||get16(b,h+4)!=8+payload.size()||b.size()!=h+8+payload.size())return false;
    return std::equal(payload.begin(),payload.end(),b.begin()+h+8);
}
int main(int argc,char** argv){
    // Input credentials are never command-line arguments, files, or output.
    std::string serverPassword=takeEnv("PCL_VLAN_TEST_PASSWORD"),roomPassword=takeEnv("PCL_VLAN_TEST_ROOM_PASSWORD");
    if(argc<7||argc>8){std::cout<<"FAIL|ARGUMENTS\n";return 2;}
    const std::string mode=argc==8?argv[7]:"relay";
    if(mode!="relay"&&mode!="default-relay"&&mode!="join-only"&&mode!="expect-wrong-password"){std::cout<<"FAIL|MODE\n";return 2;}
    if(serverPassword.empty()){std::cout<<"FAIL|MISSING_CREDENTIAL\n";return 2;}
    sock_init();int resultCode=1;
    try {
        // Keep native diagnostics separate from the probe's fixed status vocabulary.
        FILE* ignored=freopen("NUL","w",stderr);(void)ignored;
        CliLog::instance().setLogFile("");CliLog::instance().setVerbose(false);
        uint32_t port=number(argv[2],65535),room=number(argv[3],0xffffffffu),hostPeer=number(argv[4],0xffffffffu),target=number(argv[6],65535);
        IN_ADDR parsed={};if(InetPtonA(AF_INET,argv[5],&parsed)!=1)throw std::runtime_error("host vip");uint32_t hostVip=ntohl(parsed.s_addr);
        if(!port||!room||!hostPeer||!vip(hostVip)||((mode=="relay"||mode=="default-relay")&&!target))throw std::runtime_error("argument");
        std::string ip=resolve(argv[1]);CliSignalClient client;
        const bool defaultRelay=mode=="default-relay";const uint32_t serverIp=stringToIP(ip);
        UdpSocketOwner udpSocket;SecureFrameCipher udpCipher;uint32_t udpSessionId=0;
        std::unique_ptr<CliPeerConnection> defaultPeer;
        std::unique_ptr<CliRawUdpTunnel> rawTunnel;
        std::unique_ptr<CliKcpTunnel> kcpTunnel;
        bool auth=false,joined=false,relayReady=false,pong=false,echo=false,wrongRejected=false,closing=false,logoutAck=false;
        std::string error;uint32_t self=0;const uint16_t localPort=static_cast<uint16_t>(47000+(GetCurrentProcessId()%1000));
        const Buffer ping={0xfe,0x01,0x1e,0xaf,0x10,0x42};Buffer expectedPong=ping;expectedPong[1]=2;
        const std::string payload="VLanRemoteUdpEcho-"+std::to_string(GetCurrentProcessId())+"-"+std::to_string(currentTimeMs());
        Buffer udp;uint32_t lastPing=0,lastEcho=0,lastHeartbeat=0,lastUdpKeepalive=0;bool sentPing=false,sentEcho=false;
        UdpSendFunc sendSecureUdp=[&](const uint8_t* data,size_t length,uint32_t dstIp,uint16_t dstPort){
            if(!error.empty()||udpSocket.fd==SOCK_INVALID||!udpSessionId)return;
            if(dstIp!=serverIp||dstPort!=port){error="UDP_ENDPOINT";return;}
            Buffer encrypted=udpCipher.encrypt(data,length);if(encrypted.empty()){error="UDP_ENCRYPTION_FAILED";return;}
            Buffer wire(1+SECURE_SESSION_ID_SIZE+encrypted.size());wire[0]=UDP_ENCRYPTED;writeU32BE(wire.data()+1,udpSessionId);std::copy(encrypted.begin(),encrypted.end(),wire.begin()+1+SECURE_SESSION_ID_SIZE);
            sockaddr_in destination=makeAddr(serverIp,static_cast<uint16_t>(port));int sent=sendto(udpSocket.fd,reinterpret_cast<const char*>(wire.data()),static_cast<int>(wire.size()),0,reinterpret_cast<sockaddr*>(&destination),sizeof(destination));
            const bool ok=sent==static_cast<int>(wire.size());
            if(defaultPeer&&length>=sizeof(UdpRelayHeader)){
                const UdpRelayHeader* header=reinterpret_cast<const UdpRelayHeader*>(data);
                if(header->trafficClass==TRAFFIC_TCP||header->trafficClass==TRAFFIC_UDP)defaultPeer->reportLocalTransportSend(static_cast<TrafficClass>(header->trafficClass),ok);
            }
            if(!ok)error="UDP_SEND_FAILED";
        };
        auto keepalive=[&](){const uint8_t kind=UDP_KEEPALIVE;sendSecureUdp(&kind,1,serverIp,static_cast<uint16_t>(port));lastUdpKeepalive=currentTimeMs();};
        client.onSecureSessionEstablished=[&](uint32_t id,const Buffer& master){if(defaultRelay){if(!id||master.size()!=SECURE_KEY_SIZE){error="UDP_SECURITY_CONTEXT";return;}udpSessionId=id;udpCipher.init(master.data(),true,"udp");}};
        client.setServerPassword(serverPassword);if(!serverPassword.empty())SecureZeroMemory(&serverPassword[0],serverPassword.size());serverPassword.clear();
        client.onConnected=[&](){auth=true;std::cout<<"PASS|authenticated\n"<<std::flush;client.login("PCLProbe"+std::to_string(GetCurrentProcessId())+std::to_string(currentTimeMs()));};
        client.onLoginResponse=[&](uint32_t,bool){std::cout<<"PASS|logged_in\n"<<std::flush;client.joinRoom(room,roomPassword);if(!roomPassword.empty())SecureZeroMemory(&roomPassword[0],roomPassword.size());roomPassword.clear();};
        client.onJoinResponse=[&](uint32_t id,uint32_t assigned,RoomTrafficPolicy tcp,RoomTrafficPolicy udpPolicy,uint16_t roomMtu,bool protectedRoom,const std::vector<PeerInfo>& members,const Buffer&){
            if(mode=="expect-wrong-password"){error="UNEXPECTED_JOIN_SUCCESS";return;}
            if(id!=room||!vip(assigned)||assigned==hostVip){error="ROOM_IDENTITY";return;}
            bool hostFound=false;for(const auto& peer:members)if(peer.peerId==hostPeer&&peer.virtualIP==hostVip)hostFound=true;
            if(!hostFound){error="HOST_IDENTITY";return;}
            if(mode=="relay"&&(tcp.transportMode!=MODE_RELAY_TCP||udpPolicy.transportMode!=MODE_RELAY_TCP)){error="ROOM_NOT_TCP_RELAY";return;}
            if(defaultRelay&&(tcp.transportMode!=MODE_RELAY_RAW_UDP||udpPolicy.transportMode!=MODE_RELAY_KCP)){error="ROOM_NOT_DEFAULT_TRANSPORT";return;}
            self=assigned;joined=true;std::cout<<(protectedRoom?"PASS|joined_protected\n":"PASS|joined_unprotected\n")<<std::flush;
            if(mode=="relay"||defaultRelay){
                udp=udpPacket(self,hostVip,localPort,static_cast<uint16_t>(target),payload);
                if(defaultRelay){
                    if(!udpSocket.open()){error="UDP_SOCKET";return;}
                    defaultPeer.reset(new CliPeerConnection(hostPeer,hostVip,"Host"));
                    rawTunnel.reset(new CliRawUdpTunnel(sendSecureUdp,serverIp,static_cast<uint16_t>(port),tcp.fecMode,roomMtu,TRAFFIC_TCP,true));
                    rawTunnel->setRelayMode(client.myPeerId(),hostPeer);
                    defaultPeer->setRawUdpTunnel(TRAFFIC_TCP,rawTunnel.get());defaultPeer->setTransport(TRAFFIC_TCP,TRANSPORT_RELAY_RAW_UDP);
                    uint32_t conv=client.myPeerId()^hostPeer^(static_cast<uint32_t>(TRAFFIC_UDP)<<24);if(conv==0)conv=1;
                    kcpTunnel.reset(new CliKcpTunnel(conv,sendSecureUdp,serverIp,static_cast<uint16_t>(port),udpPolicy.fecMode,roomMtu,udpPolicy.kcpProfile,TRAFFIC_UDP,true));
                    kcpTunnel->setRelayMode(client.myPeerId(),hostPeer);
                    defaultPeer->setKcpTunnel(TRAFFIC_UDP,kcpTunnel.get());defaultPeer->setTransport(TRAFFIC_UDP,TRANSPORT_RELAY_KCP);
                    defaultPeer->setOnDataReceived([&](uint32_t peer,const Buffer& data){if(peer==hostPeer&&!echo&&matchesEcho(data,hostVip,self,static_cast<uint16_t>(target),localPort,payload)){echo=true;std::cout<<"PASS|remote_kcp_relay_wintun_udp_echo\n"<<std::flush;}});
                    keepalive();
                }
                client.requestRelay(hostPeer);
            }
        };
        client.onRelayReady=[&](uint32_t peer){if(peer==hostPeer){relayReady=true;std::cout<<"PASS|relay_ready\n"<<std::flush;}};
        client.onRelayData=[&](uint32_t src,TrafficClass cls,Buffer data){
            if(defaultRelay)return; // Default mode must pass over authenticated UDP, never signal fallback.
            if(src!=hostPeer||cls!=TRAFFIC_UDP)return;
            if(data.size()==6&&data[0]==0xfe&&data[1]==1){data[1]=2;client.sendRelayData(client.myPeerId(),hostPeer,TRAFFIC_UDP,data);return;}
            if(data==expectedPong&&!pong){pong=true;std::cout<<"PASS|remote_tcp_relay_pong\n"<<std::flush;return;}
            if(!echo&&matchesEcho(data,hostVip,self,static_cast<uint16_t>(target),localPort,payload)){echo=true;std::cout<<"PASS|remote_tcp_relay_wintun_udp_echo\n"<<std::flush;}
        };
        client.onLogoutAck=[&](){logoutAck=true;};
        client.onFatalAuthError=[&](uint16_t code,const std::string&){if(closing)return;if(mode=="expect-wrong-password"&&code==static_cast<uint16_t>(AuthErrorCode::RoomPassword)){wrongRejected=true;std::cout<<"PASS|room_password_rejected\n"<<std::flush;}else error="AUTHENTICATION_FAILED";};
        client.onServerError=[&](const std::string&){if(!closing&&!wrongRejected&&error.empty())error="SERVER_REJECTED";};
        client.onRoomOperationFailure=[&](const std::string&){if(!closing&&!wrongRejected&&error.empty())error="ROOM_OPERATION_FAILED";};
        client.onConnectFailed=[&](const std::string&){if(!closing)error="CONNECTION_FAILED";};
        client.onDisconnected=[&](){if(!closing&&!wrongRejected)error="DISCONNECTED";};
        auto pump=[&](){
            socket_t fd=client.fd();if(fd!=SOCK_INVALID){fd_set reads,writes;FD_ZERO(&reads);FD_ZERO(&writes);FD_SET(fd,&reads);if(client.wantsWrite())FD_SET(fd,&writes);if(udpSocket.fd!=SOCK_INVALID)FD_SET(udpSocket.fd,&reads);timeval timeout={0,20000};int n=select(0,&reads,&writes,nullptr,&timeout);if(n<0){if(!closing)error="SOCKET_SELECT";return;}if(FD_ISSET(fd,&writes))client.onWritable();if(client.fd()==fd&&FD_ISSET(fd,&reads))client.onReadable();
                if(udpSocket.fd!=SOCK_INVALID&&FD_ISSET(udpSocket.fd,&reads)){
                    for(;;){uint8_t wire[65536];sockaddr_in sender={};int senderSize=sizeof(sender);int count=recvfrom(udpSocket.fd,reinterpret_cast<char*>(wire),sizeof(wire),0,reinterpret_cast<sockaddr*>(&sender),&senderSize);if(count<=0)break;
                        if(ntohl(sender.sin_addr.s_addr)!=serverIp||ntohs(sender.sin_port)!=port||count<1+SECURE_SESSION_ID_SIZE+SECURE_FRAME_OVERHEAD||wire[0]!=UDP_ENCRYPTED||readU32BE(wire+1)!=udpSessionId)continue;
                        Buffer plain;if(!udpCipher.decrypt(wire+1+SECURE_SESSION_ID_SIZE,count-1-SECURE_SESSION_ID_SIZE,&plain)||plain.size()<sizeof(UdpRelayHeader))continue;
                        const UdpRelayHeader* header=reinterpret_cast<const UdpRelayHeader*>(plain.data());
                        if(ntohl(header->srcPeerId)!=hostPeer||ntohl(header->dstPeerId)!=client.myPeerId())continue;
                        const char* body=reinterpret_cast<const char*>(plain.data()+sizeof(UdpRelayHeader));int bodySize=static_cast<int>(plain.size()-sizeof(UdpRelayHeader));
                        if(header->type==UDP_RAW_RELAY_DATA&&header->trafficClass==TRAFFIC_TCP&&rawTunnel)rawTunnel->feedInput(body,bodySize);
                        else if(header->type==UDP_RELAY_DATA&&header->trafficClass==TRAFFIC_UDP&&kcpTunnel)kcpTunnel->feedInput(body,bodySize);
                    }
                }
            }else Sleep(10);
            client.checkTimeouts();
            if(!closing&&defaultRelay&&joined){
                if(rawTunnel)rawTunnel->update();if(kcpTunnel)kcpTunnel->update();
                if(defaultPeer&&!pong&&defaultPeer->qualitySnapshot(TRAFFIC_TCP).replies>0){pong=true;std::cout<<"PASS|remote_raw_udp_relay_pong\n"<<std::flush;}
                if(currentTimeMs()-lastUdpKeepalive>=5000)keepalive();
            }
        };
        if(!client.connectTo(ip,static_cast<uint16_t>(port)))error="CONNECTION_FAILED";
        const uint32_t start=currentTimeMs();
        while(error.empty()&&currentTimeMs()-start<28000){
            pump();const uint32_t now=currentTimeMs();
            if(wrongRejected||(joined&&mode=="join-only")||(pong&&echo))break;
            if(auth&&now-lastHeartbeat>=5000){lastHeartbeat=now;client.sendPing();}
            if(joined&&relayReady&&(!sentPing||now-lastPing>=500)&&!pong){lastPing=now;sentPing=true;if(defaultRelay)defaultPeer->sendLatencyPing(TRAFFIC_TCP);else client.sendRelayData(client.myPeerId(),hostPeer,TRAFFIC_UDP,ping);}
            if((pong||(defaultRelay&&joined&&relayReady))&&!echo&&(!sentEcho||now-lastEcho>=1000)){lastEcho=now;sentEcho=true;if(defaultRelay){if(defaultPeer->sendData(udp)<0)error="KCP_QUEUE_FAILED";}else client.sendRelayData(client.myPeerId(),hostPeer,TRAFFIC_UDP,udp);}
        }
        bool pass=error.empty()&&(wrongRejected||(joined&&mode=="join-only")||(pong&&echo));
        if(!pass&&error.empty())error=!auth?"AUTH_TIMEOUT":!joined?"JOIN_TIMEOUT":!pong?(defaultRelay?"RAW_UDP_RELAY_TIMEOUT":"RELAY_TIMEOUT"):(defaultRelay?"KCP_UDP_ECHO_TIMEOUT":"UDP_ECHO_TIMEOUT");
        const bool logoutRequired=client.myPeerId()!=0;
        closing=true;if(logoutRequired&&client.isConnected()){
            client.logout();uint32_t deadline=currentTimeMs();while(!logoutAck&&client.isConnected()&&currentTimeMs()-deadline<5000)pump();
        }
        if(logoutRequired&&logoutAck)std::cout<<"PASS|logout_acknowledged\n";
        if(pass&&logoutRequired&&!logoutAck){pass=false;error="LOGOUT_ACK_TIMEOUT";}
        client.disconnect();
        if(pass){std::cout<<"PASS|probe_session_closed\nRESULT|PASS\n";resultCode=0;}else std::cout<<"FAIL|"<<error<<"\nRESULT|FAIL\n";
    } catch(const std::exception&){std::cout<<"FAIL|LOCAL_PROBE_SETUP\nRESULT|FAIL\n";}
    sock_cleanup();return resultCode;
}
