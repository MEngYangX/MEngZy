## Get5 面板

MEngZy可以与Get5 Web面板（[G5V](https://github.com/PhlexPlexico/G5V)和[G5API](https://github.com/PhlexPlexico/G5API)）配合使用，以设置和管理比赛！

### 功能

1. 从网页面板创建队伍和设置比赛
2. 支持BO1，BO3，BO5等带有Veto和刀局的系列赛
2. 在面板上实时获取veto、比分和玩家统计信息
3. 自动将demo上传到面板（可以从其比赛页面下载）
4. 从面板暂停和取消暂停游戏
5. 在正在进行的游戏中添加玩家
6. 以及更多功能！！！

### 如何将Get5面板与MEngZy一起使用？

这很简单，只需安装Get5面板，在其中添加您的服务器，您就可以像Get5 CSGO一样创建和管理比赛 :D

### 安装Get5面板

要使用Get5面板，需要[G5V](https://github.com/PhlexPlexico/G5V)和[G5API](https://github.com/PhlexPlexico/G5API)

## 不使用Docker

### 安装G5V

您可以参考此处提供的安装步骤：https://github.com/PhlexPlexico/G5V/wiki/Installation

### 安装G5API

您可以参考此处提供的安装步骤：https://github.com/PhlexPlexico/G5API/wiki


## 使用Docker

docker-compose.yml文件：

```yml title="docker-compose.yml 示例"
services:
  redis:
    image: redis:6
    command: redis-server --requirepass Z3fZeK9W6jBfMJY
    container_name: redis
    networks:
      - get5
    restart: always

  get5db:
    image: yobasystems/alpine-mariadb
    container_name: get5db
    restart: always
    networks:
      - get5
    environment:
      - MYSQL_ROOT_PASSWORD=FJqXv2dd3TeFAn3
      - MYSQL_DATABASE=get5
      - MYSQL_USER=get5
      - MYSQL_PASSWORD=FJqXv2dd3TeFAn3
      - MYSQL_CHARSET=utf8mb4
      - MYSQL_COLLATION=utf8mb4_general_ci
    ports:
      - 3306:3306
    volumes:
      - ./get5db/mysql:/var/lib/mysql

  caddy:
    image: lucaslorentz/caddy-docker-proxy:ci-alpine
    container_name: caddy-reverse-proxy
    restart: unless-stopped
    networks:
      - get5
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    ports:
      - 80:80
      - 443:443
    environment:
      - CADDY_INGRESS_NETWORKS=get5

  g5api:
    image: ghcr.io/phlexplexico/g5api:latest
    depends_on:
      - get5db
    container_name: G5API
    networks:
      - get5
    labels:
      caddy: your-domain.com
      caddy.handle_path: /api/*
      caddy.handle_path.0_reverse_proxy: "{{upstreams 3301}}"
    volumes:
      - ./public:/Get5API/public
    environment:
      - NODE_ENV=production
      - PORT=3301
      - DBKEY=0fc9c89ce985fa8066398b1be5c730f7 #CHANGME https://www.random.org/cgi-bin/randbyte?nbytes=16&format=h
      - STEAMAPIKEY=FE315E4DAA500737EC827E9A77018971
      - HOSTNAME=https://your-domain.com
      - SHAREDSECRET= Z3TLmUEVpvXdE5H7UdnEbNSySak9gj
      - CLIENTHOME=https://your-domain.com
      - APIURL=https://your-domain.com/api
      - SQLUSER=get5
      - SQLPASSWORD=FJqXv2dd3TeFAn3
      - SQLPORT=3306
      - DATABASE=get5
      - SQLHOST=get5db
      - ADMINS=76561198154367261
      - SUPERADMINS=76561198154367261
      - REDISURL=redis://:Z3fZeK9W6jBfMJY@redis:6379
      - REDISTTL=86400
      - USEREDIS=true
      - UPLOADDEMOS=true
      - LOCALLOGINS=false
    restart: always

  g5v:
    image: ghcr.io/phlexplexico/g5v:latest
    depends_on:
      - g5api
    container_name: G5V-Front-End
    networks:
      - get5
    restart: always
    labels:
      caddy: your-domain.com
      caddy.reverse_proxy: "{{upstreams}}"

networks:
  get5:
    external: true
```

在此文件中，需要进行以下更改：

1. 将`your-domain.com`更改为您的DNS或域名
2. 如有需要，更改MySQL和Redis密码
3. 根据需要添加`ADMINS`和`SUPERADMINS`（Steam64ID，如果要添加多个管理员，用逗号分隔）

下载并安装此yml文件的命令：

```
sudo apt-get update
apt install docker.io
apt install docker-compose

docker network create -d bridge get5
docker-compose -f /path/to/your/docker-compose-file.yml up -d
```

## Get5集成的当前限制

1. 像KAST、队友被闪、闪光弹助攻、刀杀、炸弹安装和拆除这样的统计数据缺失，将显示为0
2. 不能从面板添加教练（玩家可以输入`.coach <side>`开始教练）
3. 无法从面板列出和恢复备份（像`.stop`和`.restore <roundnumber>`这样的游戏内恢复命令将按预期工作） 