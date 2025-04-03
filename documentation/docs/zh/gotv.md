## GOTV 直播

MEngZy不会更改GOTV的直播部分，但当地图结束时，如果启用了GOTV，它会自动调整
[`mp_match_restart_delay`](https://totalcsgo.com/command/mpmatchrestartdelay)，
确保其不会短于GOTV直播完成所需的时间。

!!! warning "不要过多干预TV设置！"

    在`warmup.cfg`、`live.cfg`等文件中更改`tv_delay`或`tv_enable`将导致您的demo出现问题。
    我们建议您在服务器一般设置中设置`tv_delay`。


## 录制Demo

MEngZy会自动录制demo。录制在所有队伍准备就绪后开始，并在地图结果出现后结束。

demo路径可以使用`matchzy_demo_path <directory>/`配置。如果定义了，它不能以斜杠开头，必须以斜杠结尾。设置为空字符串则使用csgo根目录。

Demo文件将根据`matchzy_demo_name_format`命名。默认格式为：`"{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}"`

!!! info "GOTV录制的广播延迟"

    当GOTV录制停止时，服务器将其帧缓冲区刷新到磁盘。如果您有较长的`tv_delay`，这可能导致GOTV广播出现延迟或完全冻结，
    因此MEngZy会等待整个比赛广播完成后才停止录制demo。

## 自动上传

除了录制demo外，MEngZy还可以在录制停止时将它们上传到URL。您可以使用
`matchzy_demo_upload_url <upload_url>`定义上传URL。HTTP正文将是压缩的demo文件，您可以
阅读[头信息](#头信息)获取文件元数据。

示例：`matchzy_demo_upload_url "https://your-website.com/upload-endpoint"`

### 头信息

MEngZy将向其demo上传请求添加以下HTTP头：

1. `MatchZy-FileName`是demo文件的名称
2. `MatchZy-MapNumber`是系列赛中从0开始索引的地图编号
3. `MatchZy-MatchId`比赛的唯一ID


### 示例

这是一个使用[Express](https://expressjs.com/)的[Node.js](https://nodejs.org/en/)Web服务器如何读取MatchZy发送的demo上传请求的示例。

!!! warning "仅为概念验证" 
 
    这是一个简单的概念验证，不应盲目复制到生产系统中。它没有HTTPS支持，只是为了演示读取潜在大型POST请求的关键方面。

```js title="Node.js示例"
const express = require('express');
const path = require('path');
const fs = require('fs');

const app = express();
const port = 3000;

app.post('/upload', function (req, res) {

    // 读取MatchZy头信息，了解如何处理文件
    const filename = req.header('MatchZy-FileName');
    const matchId = req.header('MatchZy-MatchId');
    const mapNumber = req.header('MatchZy-MapNumber');
 
    // 将同一场比赛的所有demo放在一个文件夹中
    const folder = path.join(__dirname, 'demos', matchId);
    if (!fs.existsSync(folder)) {
       fs.mkdirSync(folder, {recursive: true});
    }
    // 创建一个流并将其指向文件，使用头中的文件名
    let writeStream = fs.createWriteStream(path.join(folder, filename));
 
    // 将请求正文写入流
    req.pipe(writeStream);
 
    // 等待请求结束并回复200
    req.on('end', () => {
       writeStream.end();
       res.status(200);
       res.end('Success');
    });
 
    // 如果写入文件时出现问题，回复500
    writeStream.on('error', function (err) {
       res.status(500);
       res.end('Error writing demo file: ' + err.message);
    });
 
 })
 
app.listen(port);
``` 