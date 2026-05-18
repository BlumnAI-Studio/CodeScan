---
date: 2026-05-18T15:00+09:00
agent: actor-toolkit-scout
type: creation
mode: log-eval
trigger: "T7 — Microsoft Orleans virtual-actor fixture로 4-툴킷 매트릭스 확장"
---

# T7 — Orleans fixture 합류 (4-툴킷 매트릭스)

## 실행 요약

[[2026-05-18-12-00-residual-improvements-audit]] 의 T7. 기존 3-툴킷 매트릭스
(Akka.NET / Akka Classic / Pekko Typed) 모두 hierarchical-actor 패러다임이라
매처 사양이 비슷한 그룹. Microsoft Orleans 는 **virtual-actor 패러다임**으로
완전히 다른 표면을 가지므로 매트릭스의 분별력을 크게 높임.

## 결과 — TestSample/csharp-orleans/

```
TestSample/csharp-orleans/
├── HelloOrleans.csproj                     (net9.0 + Microsoft.Orleans.Server 9.0.1 + Sdk + Hosting)
├── Dockerfile                              (multi-stage, mcr.microsoft.com/dotnet/sdk:9.0)
├── README.md                               (패턴 차이 + regex 한계 표 + 매처 사양)
└── src/
    ├── Program.cs                          (Host.CreateDefaultBuilder + UseOrleans + GrainFactory.GetGrain)
    ├── Models/HelloResponse.cs             (record + [GenerateSerializer])
    └── Grains/
        ├── IPersonGrain.cs                 (베이스 인터페이스 IGrainWithStringKey)
        ├── PersonGrain.cs                  (abstract base — SayHello 공통)
        ├── IEnSpeakerGrain.cs / EnSpeakerGrain.cs
        ├── IKoSpeakerGrain.cs / KoSpeakerGrain.cs
        ├── IJaSpeakerGrain.cs / JaSpeakerGrain.cs
        ├── IWorldGrain.cs                  (인터페이스)
        └── WorldGrain.cs                   (coordinator — GrainFactory.GetGrain<T>(id))
```

도메인 동일 (World coordinator + En/Ko/Ja Speaker children). 표현 패러다임만 다름:

| 측면 | Akka.NET (csharp-akka) | Orleans (csharp-orleans) |
|------|------------------------|--------------------------|
| 자식 생성/참조 | `Context.ActorOf(Props.Create<T>(...), "name")` (계층적 spawn) | `GrainFactory.GetGrain<T>(id)` (virtual-actor lookup) |
| Lifecycle | 부모 supervises, `Stop()` 명시 | 런타임이 idle deactivate |
| Identity | path (`/user/world/en`) | string key (`"Alice"`) |
| Messaging | `actor.Tell(msg)` fire-and-forget | `await grain.Method(args)` RPC |
| 베이스 마커 | `: ReceiveActor` | `: Grain, IPersonGrain` |

## 정규식 매트릭스 측정

| 엣지 | csharp-akka | csharp-orleans | 비고 |
|------|-------------|----------------|------|
| `inherits_or_implements` | ✅ 5/5 (PersonActor 베이스 + 3 Speaker + WorldActor) | ✅ **7/7** (Grain 베이스 + 3 IXxxSpeakerGrain + 3 XxxSpeakerGrain + IWorldGrain + WorldGrain + IPersonGrain + PersonGrain) — 인터페이스 마커 패턴으로 엣지 수 증가 |
| `creates` | ⚠️ 1/4 (`Props.Create(() => new T())` 우연) | ❌ 0/3 (user code에 `new XxxSpeakerGrain()` 없음 — 런타임이 활성화) |
| `imports` | ✅ `Akka.Actor` | ✅ `Orleans`, `Orleans.Hosting`, `Microsoft.Extensions.*` |
| `defines` | ✅ Receive/PreStart 등 메서드 | ✅ SayHello/HelloAll 등 메서드 |
| **virtual-actor activates** | (없음 — 패러다임 부재) | ❌ 0/3 (`GrainFactory.GetGrain<T>(id)` 정규식 못 잡음 — generic argument 추출 필요) |

## 신규 그래프 엣지 — `activates`

`spawns_child`와 의미가 다르므로 `Models/EdgeKinds.cs` 에 별도 const 추가:

```csharp
// virtual-actor (Orleans-style; lookup, not spawn)
public const string Activates = "activates";  // caller -> virtual-actor type via GrainFactory.GetGrain<T>
```

검증 테스트: `Parse_OrleansActivatesEdge_RoundTrips` + `Parse_AcceptsActorModelEdgeKinds` Theory에 추가.

## 매처 사양 (Roslyn — codescan/semantic-csharp Phase 1-B)

```csharp
if (invocation.Expression is MemberAccessExpressionSyntax
    {
        Name: GenericNameSyntax { Identifier.ValueText: "GetGrain", TypeArgumentList.Arguments: { Count: > 0 } typeArgs }
    }
    && model.GetSymbolInfo(typeArgs[0]).Symbol is INamedTypeSymbol target)
{
    var owner = invocation.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
    if (owner is not null)
        EmitEdge("class", owner.Identifier.Text, "type", target.Name, "activates", line);
}
```

→ `WorldGrain -[activates]-> IEnSpeakerGrain` 같은 엣지가 emit됨. Akka.NET 매처는
같은 fixture 그룹에서 `spawns_child`를 emit하므로 **그래프 상에서 두 패러다임이
서로 다른 엣지 종류로 구별**됨.

## 평가 (actor-toolkit-scout)

| 축 | 척도 | 결과 |
|----|------|------|
| Fixture 빌드 | 4 도커 이미지 모두 실행 가능 | ⏳ 사용자 환경 검증 (코드 검증 완료, 도커 빌드 미실행) |
| 매트릭스 일관성 | 같은 의미가 툴킷별로 어떻게 표현되는지 표로 정리 | ✅ [[actor-model-cross-toolkit]] 4-툴킷 비교 + 정규식 한계 매트릭스 갱신 |
| 매처 사양 명확도 | Roslyn 매처 의사코드 | ✅ Orleans `activates` 매처 spec 작성 |
| 신규 엣지 후보 | `activates` 추가 검토 | ✅ EdgeKinds.cs 합류 + 2 테스트 |

## 다음 단계 제안

1. **사용자 환경에서 Orleans fixture 도커 빌드 검증** — `docker build -t hello-orleans TestSample/csharp-orleans/` → 정상 출력 3개국어 응답
2. **Phase 1-B 매처 추가** — docker/semantic-csharp/Program.cs 에 `GrainFactory.GetGrain<T>` 매처 추가 (Akka.NET 매처와 같은 이미지에서 처리)
3. **PostgreSQL/Redis grain storage 확장** — 메모리 grain storage 외 다양한 backend 테스트 (별도 fixture 또는 fixture 옵션화)

## 관련

- 출처 audit: [[2026-05-18-12-00-residual-improvements-audit]] (T7)
- 4-툴킷 매트릭스: [[actor-model-cross-toolkit]]
- 매처 합류 대기: [[semantic-analyzer-docker]] Phase 1-B
- 동기 합류: [[2026-05-18-15-30-phase2-foundation]] (Phase 2 도커 이미지)
