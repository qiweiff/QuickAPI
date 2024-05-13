# QuickAPI

### 您可以通过此类库快速创建一个提供api服务的程序

可以通过一行代码监听一个端口并处理事件

```
//监听端口
QuickAPI.QuickAPI.CreateAPI(port, Api);

//收到api请求时触发的事件
public static void Api(QuickAPI.QuickAPI.API.Request args)
{
       //在这里处理
}
```

## 欢迎提出改进建议
